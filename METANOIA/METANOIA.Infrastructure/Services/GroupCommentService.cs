using System.Text.RegularExpressions;
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Bình luận trên việc của nhóm. Viewer chỉ đọc; Leader và Member viết được.
    // Nhắc tên bằng @email của một thành viên trong nội dung: người đó nhận thông báo GroupMention.
    public partial class GroupCommentService : IGroupCommentService
    {
        private const int MaxContentLength = 4000;

        [GeneratedRegex(@"@([A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,})")]
        private static partial Regex MentionRegex();

        private readonly IGroupWorkRepository _work;
        private readonly IGroupRepository _groups;
        private readonly IGroupAccessService _access;
        private readonly IGroupNotifier _notifier;
        private readonly IUnitOfWork _unitOfWork;

        public GroupCommentService(
            IGroupWorkRepository work,
            IGroupRepository groups,
            IGroupAccessService access,
            IGroupNotifier notifier,
            IUnitOfWork unitOfWork)
        {
            _work = work;
            _groups = groups;
            _access = access;
            _notifier = notifier;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<GroupCommentDto>> GetCommentsAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default)
        {
            var task = await GetTaskOrThrowAsync(taskId, cancellationToken);
            await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);

            var comments = await _work.GetCommentsAsync(taskId, cancellationToken);
            return comments.Select(ToDto).ToList();
        }

        public async Task<GroupCommentDto> CreateCommentAsync(Guid userId, Guid taskId, GroupCommentRequestDto request, CancellationToken cancellationToken = default)
        {
            var task = await GetTaskOrThrowAsync(taskId, cancellationToken);
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireCanWork();
            access.RequireActive();
            var content = ValidateContent(request.Content);

            var comment = new GroupTaskComment
            {
                Id = Guid.NewGuid(),
                GroupTaskId = taskId,
                AuthorId = userId,
                Content = content,
                CreatedAt = DateTime.UtcNow
            };

            await _work.AddCommentAsync(comment, cancellationToken);
            await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.Comment, comment.Id, "Created", null, content), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var author = (await _work.GetUserNamesAsync([userId], cancellationToken)).GetValueOrDefault(userId, string.Empty);
            await NotifyMentionsAsync(access, task, author, content, cancellationToken);

            return new GroupCommentDto
            {
                Id = comment.Id,
                GroupTaskId = taskId,
                AuthorId = userId,
                AuthorName = author,
                Content = comment.Content,
                CreatedAt = comment.CreatedAt
            };
        }

        public async Task<GroupCommentDto> UpdateCommentAsync(Guid userId, Guid commentId, GroupCommentRequestDto request, CancellationToken cancellationToken = default)
        {
            var comment = await GetCommentOrThrowAsync(commentId, cancellationToken);
            var access = await _access.GetAccessAsync(comment.GroupTask.GroupId, userId, cancellationToken);
            access.RequireActive();

            if (comment.AuthorId != userId)
            {
                throw new GroupForbiddenException("Chỉ tác giả mới được sửa bình luận.");
            }

            comment.Content = ValidateContent(request.Content);
            comment.EditedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(comment);
        }

        public async Task DeleteCommentAsync(Guid userId, Guid commentId, CancellationToken cancellationToken = default)
        {
            var comment = await GetCommentOrThrowAsync(commentId, cancellationToken);
            var access = await _access.GetAccessAsync(comment.GroupTask.GroupId, userId, cancellationToken);
            access.RequireActive();

            if (comment.AuthorId != userId && !access.IsLeader)
            {
                throw new GroupForbiddenException("Chỉ tác giả hoặc Leader mới được xóa bình luận.");
            }

            _work.RemoveComment(comment);
            await _work.AddActivityAsync(GroupRules.Log(comment.GroupTask.GroupId, userId, GroupEntityTypes.Comment, comment.Id, "Deleted", comment.Content, null), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task NotifyMentionsAsync(GroupAccess access, GroupTask task, string authorName, string content, CancellationToken cancellationToken)
        {
            var emails = MentionRegex().Matches(content)
                .Select(m => m.Groups[1].Value.ToLowerInvariant())
                .Distinct()
                .ToList();
            if (emails.Count == 0)
            {
                return;
            }

            var members = await _groups.GetMembersAsync(access.Group.Id, cancellationToken);
            foreach (var member in members.Where(m => m.UserId != access.UserId && emails.Contains(m.Email.ToLowerInvariant())))
            {
                await _notifier.NotifyAsync(member.UserId, GroupNotificationTypes.Mention, "Bạn được nhắc tên",
                    $"{authorName} nhắc tên bạn trong việc \"{task.Title}\" của nhóm \"{access.Group.Name}\".", access.Group.Id, task.Id);
            }
        }

        private async Task<GroupTask> GetTaskOrThrowAsync(Guid taskId, CancellationToken cancellationToken)
        {
            return await _work.GetTaskAsync(taskId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy việc.");
        }

        private async Task<GroupTaskComment> GetCommentOrThrowAsync(Guid commentId, CancellationToken cancellationToken)
        {
            return await _work.GetCommentAsync(commentId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy bình luận.");
        }

        private static string ValidateContent(string? content)
        {
            var trimmed = content?.Trim() ?? string.Empty;
            if (trimmed.Length == 0 || trimmed.Length > MaxContentLength)
            {
                throw new UserFriendlyException($"Nội dung bình luận không được để trống và tối đa {MaxContentLength} ký tự.");
            }

            return trimmed;
        }

        private static GroupCommentDto ToDto(GroupTaskComment comment)
        {
            return new GroupCommentDto
            {
                Id = comment.Id,
                GroupTaskId = comment.GroupTaskId,
                AuthorId = comment.AuthorId,
                AuthorName = comment.Author.FullName,
                Content = comment.Content,
                CreatedAt = comment.CreatedAt,
                EditedAt = comment.EditedAt
            };
        }
    }
}
