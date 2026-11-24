using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Admin quản trị nhóm của nhánh Group: xem, thống kê, lưu trữ, khôi phục.
    // Mọi lần đổi trạng thái đều ghi AdminActionLog (bảng WorkGroup nằm trong danh sách được ghi), kèm lý do, và ghi vào nhật ký của nhóm.
    // Admin không đọc bình luận hay lịch cá nhân của thành viên.
    public class AdminGroupService : IAdminGroupService
    {
        private const int MaxActivityTake = 200;

        private readonly IAdminGroupRepository _adminGroups;
        private readonly IGroupRepository _groups;
        private readonly IGroupWorkRepository _work;
        private readonly IGroupProgressService _progress;
        private readonly IGroupNotifier _notifier;
        private readonly IUnitOfWork _unitOfWork;

        public AdminGroupService(
            IAdminGroupRepository adminGroups,
            IGroupRepository groups,
            IGroupWorkRepository work,
            IGroupProgressService progress,
            IGroupNotifier notifier,
            IUnitOfWork unitOfWork)
        {
            _adminGroups = adminGroups;
            _groups = groups;
            _work = work;
            _progress = progress;
            _notifier = notifier;
            _unitOfWork = unitOfWork;
        }

        public async Task<PagedResultDto<AdminGroupListItemDto>> SearchAsync(
            string? search, string? status, Guid? ownerId, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var (p, size) = Paging.Normalize(page, pageSize);
            if (!string.IsNullOrWhiteSpace(status) && status != GroupStatuses.Active && status != GroupStatuses.Archived)
            {
                throw new UserFriendlyException("status không hợp lệ. Chọn Active hoặc Archived.");
            }

            var (items, total) = await _adminGroups.SearchAsync(search, string.IsNullOrWhiteSpace(status) ? null : status, ownerId, p, size, cancellationToken);
            return new PagedResultDto<AdminGroupListItemDto> { Items = items.ToList(), Page = p, PageSize = size, TotalCount = total };
        }

        public Task<AdminGroupDetailDto> GetAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            return BuildDetailAsync(groupId, cancellationToken);
        }

        public async Task<IReadOnlyList<GroupActivityDto>> GetActivityAsync(
            Guid groupId, int skip, int take, CancellationToken cancellationToken = default)
        {
            _ = await _adminGroups.GetGroupWithOwnerAsync(groupId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy nhóm.");

            var rows = await _work.GetActivityAsync(groupId, Math.Max(0, skip), Math.Clamp(take, 1, MaxActivityTake), cancellationToken);
            return rows.Select(l => new GroupActivityDto
            {
                Id = l.Id,
                ActorId = l.ActorId,
                ActorName = l.Actor.FullName,
                EntityType = l.EntityType,
                EntityId = l.EntityId,
                Action = l.Action,
                OldValue = l.OldValue,
                NewValue = l.NewValue,
                CreatedAt = l.CreatedAt
            }).ToList();
        }

        public Task<AdminGroupStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
        {
            return _adminGroups.GetStatsAsync(DateTime.UtcNow, cancellationToken);
        }

        public Task<AdminGroupDetailDto> ArchiveAsync(Guid adminId, Guid groupId, string reason, CancellationToken cancellationToken = default)
        {
            return SetStatusAsync(adminId, groupId, GroupStatuses.Archived, reason, cancellationToken);
        }

        public Task<AdminGroupDetailDto> RestoreAsync(Guid adminId, Guid groupId, string reason, CancellationToken cancellationToken = default)
        {
            return SetStatusAsync(adminId, groupId, GroupStatuses.Active, reason, cancellationToken);
        }

        private async Task<AdminGroupDetailDto> SetStatusAsync(
            Guid adminId, Guid groupId, string newStatus, string reason, CancellationToken cancellationToken)
        {
            var group = await _groups.GetGroupAsync(groupId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy nhóm.");

            if (group.Status == newStatus)
            {
                throw new UserFriendlyException(newStatus == GroupStatuses.Archived
                    ? "Nhóm đã được lưu trữ rồi."
                    : "Nhóm đang hoạt động, không cần khôi phục.");
            }

            var archiving = newStatus == GroupStatuses.Archived;
            var oldStatus = group.Status;
            group.Status = newStatus;
            await _work.AddActivityAsync(GroupRules.Log(groupId, adminId, GroupEntityTypes.Group, groupId,
                archiving ? "AdminArchived" : "AdminRestored", oldStatus, $"{newStatus}. Lý do: {reason}"), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Báo cho Leader biết vì sao nhóm của họ bị đổi trạng thái (gửi sau khi đã lưu, lỗi gửi không làm hỏng thao tác)
            var title = archiving ? "Nhóm đã bị quản trị viên lưu trữ" : "Nhóm đã được quản trị viên khôi phục";
            var message = $"Nhóm \"{group.Name}\" {(archiving ? "đã bị lưu trữ" : "đã được khôi phục")} bởi quản trị viên. Lý do: {reason}";
            foreach (var leaderId in await _groups.GetLeaderIdsAsync(groupId, cancellationToken))
            {
                await _notifier.NotifyAsync(leaderId, GroupNotificationTypes.StatusChanged, title, message, groupId);
            }

            return await BuildDetailAsync(groupId, cancellationToken);
        }

        private async Task<AdminGroupDetailDto> BuildDetailAsync(Guid groupId, CancellationToken cancellationToken)
        {
            var group = await _adminGroups.GetGroupWithOwnerAsync(groupId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy nhóm.");

            return new AdminGroupDetailDto
            {
                Id = group.Id,
                Name = group.Name,
                Description = group.Description,
                Status = group.Status,
                OwnerId = group.OwnerId,
                OwnerName = group.Owner.FullName,
                OwnerEmail = group.Owner.Email,
                StartDate = group.StartDate,
                Deadline = group.Deadline,
                CreatedAt = group.CreatedAt,
                Members = (await _groups.GetMembersAsync(groupId, cancellationToken)).ToList(),
                Progress = await _progress.GetProgressForAdminAsync(groupId, cancellationToken)
            };
        }
    }
}
