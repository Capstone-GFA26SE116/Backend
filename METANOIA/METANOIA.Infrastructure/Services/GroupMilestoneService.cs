using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Giai đoạn của nhóm: giống MilestoneService của nhánh cá nhân, nhưng chỉ Leader ghi được.
    public class GroupMilestoneService : IGroupMilestoneService
    {
        private const int MaxNameLength = 200;

        private readonly IGroupWorkRepository _work;
        private readonly IGroupAccessService _access;
        private readonly IUnitOfWork _unitOfWork;

        public GroupMilestoneService(IGroupWorkRepository work, IGroupAccessService access, IUnitOfWork unitOfWork)
        {
            _work = work;
            _access = access;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<GroupMilestoneDto>> GetMilestonesAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default)
        {
            await _access.GetAccessAsync(groupId, userId, cancellationToken);
            var milestones = await _work.GetMilestonesAsync(groupId, cancellationToken);
            return milestones.Select(ToDto).ToList();
        }

        public async Task<GroupMilestoneDto> GetMilestoneAsync(Guid userId, Guid milestoneId, CancellationToken cancellationToken = default)
        {
            var milestone = await GetMilestoneOrThrowAsync(milestoneId, cancellationToken);
            await _access.GetAccessAsync(milestone.GroupId, userId, cancellationToken);
            return ToDto(milestone);
        }

        public async Task<GroupMilestoneDto> CreateMilestoneAsync(Guid userId, Guid groupId, GroupMilestoneRequestDto request, CancellationToken cancellationToken = default)
        {
            var access = await _access.GetAccessAsync(groupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();
            ValidateRequest(request);
            await EnsureOrderIndexFreeAsync(groupId, request.OrderIndex, Guid.Empty, cancellationToken);

            var status = string.IsNullOrWhiteSpace(request.Status) ? GroupWorkStatuses.Planned : request.Status;
            var milestone = new GroupMilestone
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                Name = request.Name.Trim(),
                OrderIndex = request.OrderIndex,
                DueDate = request.DueDate,
                Status = status,
                DeliveredAt = status == GroupWorkStatuses.Delivered ? DateTime.UtcNow : null
            };

            await _work.AddMilestoneAsync(milestone, cancellationToken);
            await _work.AddActivityAsync(GroupRules.Log(groupId, userId, GroupEntityTypes.Milestone, milestone.Id, "Created", null, milestone.Name), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(milestone);
        }

        public async Task<GroupMilestoneDto> UpdateMilestoneAsync(Guid userId, Guid milestoneId, GroupMilestoneRequestDto request, CancellationToken cancellationToken = default)
        {
            var milestone = await GetMilestoneOrThrowAsync(milestoneId, cancellationToken);
            var access = await _access.GetAccessAsync(milestone.GroupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();
            ValidateRequest(request);
            await EnsureOrderIndexFreeAsync(milestone.GroupId, request.OrderIndex, milestoneId, cancellationToken);

            // Không gửi status thì giữ nguyên
            var oldStatus = milestone.Status;
            var newStatus = string.IsNullOrWhiteSpace(request.Status) ? oldStatus : request.Status;
            GroupRules.EnsureMilestone(oldStatus, newStatus);

            // Còn việc chưa Done hoặc Skipped thì cảnh báo (409) kèm danh sách; Leader làm tiếp hoặc xác nhận vẫn giao (ConfirmOpenTasks)
            if (newStatus == GroupWorkStatuses.Delivered && oldStatus != GroupWorkStatuses.Delivered && !request.ConfirmOpenTasks)
            {
                var open = await _work.GetOpenTasksAsync(milestoneId, cancellationToken);
                if (open.Count > 0)
                {
                    var subTasks = open.Sum(t => t.OpenSubTaskCount);
                    throw new MilestoneOpenTasksException(
                        $"Còn {open.Count} việc chưa Done hoặc Skipped" + (subTasks > 0 ? $" (trong đó {subTasks} việc con chưa xong)" : "") +
                        ". Hãy làm tiếp, hoặc gửi lại kèm confirmOpenTasks = true để vẫn giao giai đoạn.",
                        new { openTaskCount = open.Count, openSubTaskCount = subTasks, tasks = open });
                }
            }

            var oldName = milestone.Name;
            milestone.Name = request.Name.Trim();
            milestone.OrderIndex = request.OrderIndex;
            milestone.DueDate = request.DueDate;

            if (newStatus != oldStatus)
            {
                milestone.Status = newStatus;
                if (newStatus == GroupWorkStatuses.Delivered) milestone.DeliveredAt = DateTime.UtcNow;
                if (newStatus == GroupWorkStatuses.InRevision) milestone.RevisionCount += 1;
            }

            await _work.AddActivityAsync(newStatus != oldStatus
                ? GroupRules.Log(milestone.GroupId, userId, GroupEntityTypes.Milestone, milestone.Id, "StatusChanged", oldStatus, newStatus)
                : GroupRules.Log(milestone.GroupId, userId, GroupEntityTypes.Milestone, milestone.Id, "Updated", oldName, milestone.Name), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(milestone);
        }

        public async Task DeleteMilestoneAsync(Guid userId, Guid milestoneId, CancellationToken cancellationToken = default)
        {
            var milestone = await GetMilestoneOrThrowAsync(milestoneId, cancellationToken);
            var access = await _access.GetAccessAsync(milestone.GroupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();

            _work.RemoveMilestone(milestone);
            await _work.AddActivityAsync(GroupRules.Log(milestone.GroupId, userId, GroupEntityTypes.Milestone, milestone.Id, "Deleted", milestone.Name, null), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task<GroupMilestone> GetMilestoneOrThrowAsync(Guid milestoneId, CancellationToken cancellationToken)
        {
            return await _work.GetMilestoneAsync(milestoneId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy giai đoạn.");
        }

        private async Task EnsureOrderIndexFreeAsync(Guid groupId, int orderIndex, Guid excludeId, CancellationToken cancellationToken)
        {
            if (await _work.MilestoneOrderIndexTakenAsync(groupId, orderIndex, excludeId, cancellationToken))
            {
                throw new UserFriendlyException("Số thứ tự giai đoạn đã tồn tại trong nhóm này.");
            }
        }

        private static void ValidateRequest(GroupMilestoneRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength)
            {
                throw new UserFriendlyException($"Tên giai đoạn không được để trống và tối đa {MaxNameLength} ký tự.");
            }

            if (request.OrderIndex < 1)
            {
                throw new UserFriendlyException("Số thứ tự giai đoạn phải từ 1 trở lên.");
            }

            if (!string.IsNullOrWhiteSpace(request.Status) && !GroupWorkStatuses.IsMilestoneStatus(request.Status))
            {
                throw new UserFriendlyException("Trạng thái không hợp lệ. Chọn một trong: Planned, InProgress, Delivered, InRevision, Approved.");
            }
        }

        private static GroupMilestoneDto ToDto(GroupMilestone milestone)
        {
            return new GroupMilestoneDto
            {
                Id = milestone.Id,
                GroupId = milestone.GroupId,
                Name = milestone.Name,
                OrderIndex = milestone.OrderIndex,
                DueDate = milestone.DueDate,
                Status = milestone.Status,
                DeliveredAt = milestone.DeliveredAt,
                RevisionCount = milestone.RevisionCount
            };
        }
    }
}
