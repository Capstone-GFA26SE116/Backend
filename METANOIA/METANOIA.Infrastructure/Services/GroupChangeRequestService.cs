using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Đề xuất đổi giờ cố định hoặc deadline (docs/GROUP_FEATURE.md mục 11). Member không tự sửa giờ của việc đã giao:
    // người được giao đề xuất kèm lý do, Leader duyệt (áp dụng vào việc) hoặc từ chối.
    public class GroupChangeRequestService : IGroupChangeRequestService
    {
        private const int MaxReasonLength = 1000;

        private readonly IGroupWorkRepository _work;
        private readonly IGroupRepository _groups;
        private readonly IGroupAccessService _access;
        private readonly IGroupAvailabilityService _availability;
        private readonly IGroupNotifier _notifier;
        private readonly IUnitOfWork _unitOfWork;
        private readonly TimeProvider _timeProvider;

        public GroupChangeRequestService(
            IGroupWorkRepository work,
            IGroupRepository groups,
            IGroupAccessService access,
            IGroupAvailabilityService availability,
            IGroupNotifier notifier,
            IUnitOfWork unitOfWork,
            TimeProvider timeProvider)
        {
            _work = work;
            _groups = groups;
            _access = access;
            _availability = availability;
            _notifier = notifier;
            _unitOfWork = unitOfWork;
            _timeProvider = timeProvider;
        }

        public async Task<GroupChangeRequestDto> CreateAsync(Guid userId, Guid taskId, GroupChangeRequestCreateDto request, CancellationToken cancellationToken = default)
        {
            var task = await _work.GetTaskAsync(taskId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy việc.");
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireCanWork();
            access.RequireActive();

            if (GroupRules.IsClosed(task.Status))
            {
                throw new UserFriendlyException($"Việc đang ở trạng thái '{task.Status}' nên không đổi giờ được.");
            }

            // Chỉ người đang làm việc này (được giao việc hoặc một việc con) mới đề xuất
            var mine = task.AssigneeId == userId || task.SubTasks.Any(s => s.AssigneeId == userId);
            if (!mine)
            {
                throw new GroupForbiddenException("Chỉ người được giao việc (hoặc một việc con của việc) mới đề xuất đổi giờ.");
            }

            var reason = ValidateReason(request.Reason);
            var (newStart, newEnd, newDeadline) = ValidateValues(request.NewFixedStart, request.NewFixedEnd, request.NewDeadline);

            var sameFixed = newStart == task.FixedStart && newEnd == task.FixedEnd;
            var sameDeadline = newDeadline is null || newDeadline == task.Deadline;
            if ((newStart is null || sameFixed) && sameDeadline)
            {
                throw new UserFriendlyException("Giờ đề xuất giống giờ hiện tại của việc.");
            }

            if (await _work.GetPendingChangeRequestAsync(taskId, userId, cancellationToken) is not null)
            {
                throw new UserFriendlyException("Bạn đã có một đề xuất đang chờ duyệt cho việc này. Hãy rút lại đề xuất cũ nếu muốn gửi đề xuất mới.");
            }

            var changeRequest = new GroupTaskChangeRequest
            {
                Id = Guid.NewGuid(),
                GroupTaskId = taskId,
                RequestedById = userId,
                NewFixedStart = newStart,
                NewFixedEnd = newEnd,
                NewDeadline = newDeadline,
                Reason = reason,
                Status = GroupChangeRequestStatuses.Pending,
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
            };

            await _work.AddChangeRequestAsync(changeRequest, cancellationToken);
            await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.ChangeRequest, changeRequest.Id,
                "ChangeRequested", Describe(task.FixedStart, task.FixedEnd, task.Deadline), Describe(newStart, newEnd, newDeadline)), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var names = await _work.GetUserNamesAsync([userId], cancellationToken);
            var requesterName = names.GetValueOrDefault(userId, "Một thành viên");

            foreach (var leaderId in (await _groups.GetLeaderIdsAsync(task.GroupId, cancellationToken)).Where(id => id != userId))
            {
                await _notifier.NotifyAsync(leaderId, GroupNotificationTypes.ChangeRequested, "Đề xuất đổi giờ",
                    $"{requesterName} đề xuất đổi giờ việc \"{task.Title}\" của nhóm \"{access.Group.Name}\": {reason}", task.GroupId, task.Id);
            }

            return ToDto(changeRequest, task, requesterName, null);
        }

        public async Task<IReadOnlyList<GroupChangeRequestDto>> GetOfGroupAsync(Guid userId, Guid groupId, string? status, CancellationToken cancellationToken = default)
        {
            var access = await _access.GetAccessAsync(groupId, userId, cancellationToken);
            access.RequireCanWork();

            if (!string.IsNullOrWhiteSpace(status) && !GroupChangeRequestStatuses.IsValid(status))
            {
                throw new UserFriendlyException("Trạng thái không hợp lệ. Chọn một trong: Pending, Approved, Rejected, Cancelled.");
            }

            // Leader thấy mọi đề xuất; Member chỉ thấy đề xuất của mình
            var rows = await _work.GetChangeRequestsOfGroupAsync(groupId, status, access.IsLeader ? null : userId, cancellationToken);
            return rows.Select(r => ToDto(r, r.GroupTask, r.RequestedBy.FullName, r.DecidedBy?.FullName)).ToList();
        }

        public async Task<IReadOnlyList<GroupChangeRequestDto>> GetOfTaskAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default)
        {
            var task = await _work.GetTaskAsync(taskId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy việc.");
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireCanWork();

            var rows = await _work.GetChangeRequestsOfTaskAsync(taskId, access.IsLeader ? null : userId, cancellationToken);
            return rows.Select(r => ToDto(r, r.GroupTask, r.RequestedBy.FullName, r.DecidedBy?.FullName)).ToList();
        }

        public async Task<GroupChangeRequestDto> ApproveAsync(Guid userId, Guid requestId, GroupChangeDecisionDto request, CancellationToken cancellationToken = default)
        {
            var changeRequest = await GetRequestOrThrowAsync(requestId, cancellationToken);
            var task = changeRequest.GroupTask;
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();
            EnsurePending(changeRequest);

            if (GroupRules.IsClosed(task.Status))
            {
                throw new UserFriendlyException($"Việc đang ở trạng thái '{task.Status}' nên không đổi giờ được.");
            }

            var newFixedStart = changeRequest.NewFixedStart ?? task.FixedStart;
            var newFixedEnd = changeRequest.NewFixedEnd ?? task.FixedEnd;
            var newDeadline = changeRequest.NewDeadline ?? task.Deadline;

            // Giờ mới có thể trùng lịch của người đang được giao việc: cảnh báo 409, Leader xác nhận để vẫn áp dụng
            if (task.AssigneeId is { } assigneeId)
            {
                await _availability.EnsureAvailableAsync(assigneeId, newFixedStart, newFixedEnd, newDeadline,
                    task.EstimatedMinutes, task.Id, request.ConfirmConflicts, cancellationToken);
            }

            var before = Describe(task.FixedStart, task.FixedEnd, task.Deadline);
            task.FixedStart = newFixedStart;
            task.FixedEnd = newFixedEnd;
            task.Deadline = newDeadline;

            Decide(changeRequest, GroupChangeRequestStatuses.Approved, userId, request.Note);
            await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.Task, task.Id, "Updated",
                before, Describe(task.FixedStart, task.FixedEnd, task.Deadline)), cancellationToken);
            await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.ChangeRequest, changeRequest.Id,
                "ChangeApproved", null, changeRequest.DecisionNote), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await NotifyDecisionAsync(access, changeRequest, task, "được duyệt");
            return await ToDtoAsync(changeRequest, task, userId, cancellationToken);
        }

        public async Task<GroupChangeRequestDto> RejectAsync(Guid userId, Guid requestId, GroupChangeDecisionDto request, CancellationToken cancellationToken = default)
        {
            var changeRequest = await GetRequestOrThrowAsync(requestId, cancellationToken);
            var task = changeRequest.GroupTask;
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();
            EnsurePending(changeRequest);

            var note = request.Note?.Trim();
            if (string.IsNullOrEmpty(note) || note.Length > MaxReasonLength)
            {
                throw new UserFriendlyException($"Hãy ghi lý do từ chối (tối đa {MaxReasonLength} ký tự).");
            }

            Decide(changeRequest, GroupChangeRequestStatuses.Rejected, userId, note);
            await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.ChangeRequest, changeRequest.Id,
                "ChangeRejected", null, note), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await NotifyDecisionAsync(access, changeRequest, task, "bị từ chối");
            return await ToDtoAsync(changeRequest, task, userId, cancellationToken);
        }

        public async Task<GroupChangeRequestDto> CancelAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default)
        {
            var changeRequest = await GetRequestOrThrowAsync(requestId, cancellationToken);
            var task = changeRequest.GroupTask;
            await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);

            // Đề xuất của người khác coi như không tồn tại với người không phải Leader
            if (changeRequest.RequestedById != userId)
            {
                throw new GroupForbiddenException("Chỉ người đề xuất mới được rút lại đề xuất.");
            }

            EnsurePending(changeRequest);
            changeRequest.Status = GroupChangeRequestStatuses.Cancelled;
            changeRequest.DecidedAt = _timeProvider.GetUtcNow().UtcDateTime;
            await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.ChangeRequest, changeRequest.Id,
                "ChangeCancelled", null, null), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(changeRequest, task, changeRequest.RequestedBy.FullName, null);
        }

        private async Task<GroupTaskChangeRequest> GetRequestOrThrowAsync(Guid requestId, CancellationToken cancellationToken)
        {
            return await _work.GetChangeRequestAsync(requestId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy đề xuất.");
        }

        private static void EnsurePending(GroupTaskChangeRequest changeRequest)
        {
            if (changeRequest.Status != GroupChangeRequestStatuses.Pending)
            {
                throw new UserFriendlyException("Đề xuất này đã được xử lý hoặc đã bị rút lại.");
            }
        }

        private void Decide(GroupTaskChangeRequest changeRequest, string status, Guid deciderId, string? note)
        {
            changeRequest.Status = status;
            changeRequest.DecidedById = deciderId;
            changeRequest.DecidedAt = _timeProvider.GetUtcNow().UtcDateTime;
            changeRequest.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        }

        private async Task NotifyDecisionAsync(GroupAccess access, GroupTaskChangeRequest changeRequest, GroupTask task, string outcome)
        {
            if (changeRequest.RequestedById == access.UserId)
            {
                return;
            }

            var note = string.IsNullOrWhiteSpace(changeRequest.DecisionNote) ? string.Empty : $" Ghi chú: {changeRequest.DecisionNote}";
            await _notifier.NotifyAsync(changeRequest.RequestedById, GroupNotificationTypes.ChangeDecided, "Đề xuất đổi giờ",
                $"Đề xuất đổi giờ việc \"{task.Title}\" của nhóm \"{access.Group.Name}\" {outcome}.{note}", task.GroupId, task.Id);
        }

        private async Task<GroupChangeRequestDto> ToDtoAsync(GroupTaskChangeRequest changeRequest, GroupTask task, Guid deciderId, CancellationToken cancellationToken)
        {
            var names = await _work.GetUserNamesAsync([changeRequest.RequestedById, deciderId], cancellationToken);
            return ToDto(changeRequest, task, names.GetValueOrDefault(changeRequest.RequestedById, string.Empty), names.GetValueOrDefault(deciderId));
        }

        private static string ValidateReason(string? reason)
        {
            var trimmed = reason?.Trim() ?? string.Empty;
            if (trimmed.Length == 0 || trimmed.Length > MaxReasonLength)
            {
                throw new UserFriendlyException($"Lý do không được để trống và tối đa {MaxReasonLength} ký tự.");
            }

            return trimmed;
        }

        // Giờ cố định mới gồm cả hai đầu hoặc không có đầu nào; phải đề xuất ít nhất một giá trị mới
        private static (DateTime? Start, DateTime? End, DateTime? Deadline) ValidateValues(
            DateTimeOffset? newStart, DateTimeOffset? newEnd, DateTimeOffset? newDeadline)
        {
            if ((newStart is null) != (newEnd is null))
            {
                throw new UserFriendlyException("Giờ cố định mới phải có cả giờ bắt đầu và giờ kết thúc.");
            }

            if (newStart is { } start && newEnd is { } end && end <= start)
            {
                throw new UserFriendlyException("Giờ kết thúc phải sau giờ bắt đầu.");
            }

            if (newStart is null && newDeadline is null)
            {
                throw new UserFriendlyException("Hãy đề xuất giờ cố định mới hoặc deadline mới.");
            }

            return (newStart is { } s ? UtcTime.ToStorage(s) : null,
                    newEnd is { } e ? UtcTime.ToStorage(e) : null,
                    newDeadline is { } d ? UtcTime.ToStorage(d) : null);
        }

        private static string Describe(DateTime? fixedStart, DateTime? fixedEnd, DateTime? deadline)
        {
            var parts = new List<string>();
            if (fixedStart is { } s && fixedEnd is { } e)
            {
                parts.Add($"giờ cố định {s:yyyy-MM-dd HH:mm} đến {e:yyyy-MM-dd HH:mm} UTC");
            }

            if (deadline is { } d)
            {
                parts.Add($"deadline {d:yyyy-MM-dd HH:mm} UTC");
            }

            return parts.Count == 0 ? "không có" : string.Join(", ", parts);
        }

        private static GroupChangeRequestDto ToDto(GroupTaskChangeRequest r, GroupTask task, string requesterName, string? decidedByName)
        {
            return new GroupChangeRequestDto
            {
                Id = r.Id,
                GroupTaskId = r.GroupTaskId,
                TaskTitle = task.Title,
                RequestedById = r.RequestedById,
                RequestedByName = requesterName,
                CurrentFixedStart = UtcTime.ToApi(task.FixedStart),
                CurrentFixedEnd = UtcTime.ToApi(task.FixedEnd),
                CurrentDeadline = UtcTime.ToApi(task.Deadline),
                NewFixedStart = UtcTime.ToApi(r.NewFixedStart),
                NewFixedEnd = UtcTime.ToApi(r.NewFixedEnd),
                NewDeadline = UtcTime.ToApi(r.NewDeadline),
                Reason = r.Reason,
                Status = r.Status,
                DecidedById = r.DecidedById,
                DecidedByName = decidedByName,
                DecidedAt = UtcTime.ToApi(r.DecidedAt),
                DecisionNote = r.DecisionNote,
                CreatedAt = r.CreatedAt
            };
        }
    }
}
