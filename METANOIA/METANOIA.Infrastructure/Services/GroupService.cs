using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Nhóm và thành viên của nhánh Group (docs/GROUP_FEATURE.md)
    public class GroupService : IGroupService
    {
        private const int MaxNameLength = 200;
        private const int MaxDescriptionLength = 4000;
        private const int MaxActivityTake = 200;

        private readonly IGroupRepository _groups;
        private readonly IGroupWorkRepository _work;
        private readonly IGroupAccessService _access;
        private readonly IUnitOfWork _unitOfWork;

        public GroupService(
            IGroupRepository groups,
            IGroupWorkRepository work,
            IGroupAccessService access,
            IUnitOfWork unitOfWork)
        {
            _groups = groups;
            _work = work;
            _access = access;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<GroupDto>> GetMyGroupsAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken = default)
        {
            var rows = await _groups.GetGroupsOfUserAsync(userId, includeArchived, cancellationToken);
            return rows.Select(r => ToDto(r.Group, r.Role, r.MemberCount)).ToList();
        }

        public async Task<GroupDto> GetGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default)
        {
            var access = await _access.GetAccessAsync(groupId, userId, cancellationToken);
            return ToDto(access.Group, access.Role, await _groups.CountMembersAsync(groupId, cancellationToken));
        }

        public async Task<GroupDto> CreateGroupAsync(Guid userId, GroupRequestDto request, CancellationToken cancellationToken = default)
        {
            ValidateRequest(request);

            var now = DateTime.UtcNow;
            var group = new WorkGroup
            {
                Id = Guid.NewGuid(),
                OwnerId = userId,
                Status = GroupStatuses.Active,
                CreatedAt = now
            };
            ApplyRequest(group, request);

            await _groups.AddGroupAsync(group, cancellationToken);
            await _groups.AddMemberAsync(new GroupMember
            {
                Id = Guid.NewGuid(),
                GroupId = group.Id,
                UserId = userId,
                Role = GroupRoles.Leader,
                JoinedAt = now
            }, cancellationToken);
            await _work.AddActivityAsync(GroupRules.Log(group.Id, userId, GroupEntityTypes.Group, group.Id, "Created", null, group.Name), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(group, GroupRoles.Leader, 1);
        }

        public async Task<GroupDto> UpdateGroupAsync(Guid userId, Guid groupId, GroupRequestDto request, CancellationToken cancellationToken = default)
        {
            var access = await _access.GetAccessAsync(groupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();
            ValidateRequest(request);

            var oldName = access.Group.Name;
            ApplyRequest(access.Group, request);
            await _work.AddActivityAsync(GroupRules.Log(groupId, userId, GroupEntityTypes.Group, groupId, "Updated", oldName, access.Group.Name), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(access.Group, access.Role, await _groups.CountMembersAsync(groupId, cancellationToken));
        }

        public Task<GroupDto> ArchiveGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default)
        {
            return SetStatusAsync(userId, groupId, GroupStatuses.Archived, cancellationToken);
        }

        public Task<GroupDto> RestoreGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default)
        {
            return SetStatusAsync(userId, groupId, GroupStatuses.Active, cancellationToken);
        }

        public async Task<IReadOnlyList<GroupMemberDto>> GetMembersAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default)
        {
            await _access.GetAccessAsync(groupId, userId, cancellationToken);
            return await _groups.GetMembersAsync(groupId, cancellationToken);
        }

        public async Task<GroupMemberDto> ChangeMemberRoleAsync(
            Guid userId, Guid groupId, Guid memberUserId, GroupMemberRoleRequestDto request, CancellationToken cancellationToken = default)
        {
            var access = await _access.GetAccessAsync(groupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();

            if (!GroupRoles.IsValid(request.Role))
            {
                throw new UserFriendlyException("Vai trò không hợp lệ. Chọn một trong: Leader, Member, Viewer.");
            }

            var member = await _groups.GetMemberAsync(groupId, memberUserId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy thành viên trong nhóm.");

            var oldRole = member.Role;
            if (oldRole != request.Role)
            {
                if (oldRole == GroupRoles.Leader && await _groups.CountLeadersAsync(groupId, cancellationToken) <= 1)
                {
                    throw new UserFriendlyException("Nhóm phải còn ít nhất một Leader. Hãy chọn Leader khác trước khi hạ vai trò này.");
                }

                member.Role = request.Role;

                // Viewer không được nhận việc: việc đang giao cho họ về chưa giao
                if (request.Role == GroupRoles.Viewer)
                {
                    await _groups.ClearAssignmentsAsync(groupId, memberUserId, cancellationToken);
                }

                await _work.AddActivityAsync(GroupRules.Log(groupId, userId, GroupEntityTypes.Member, memberUserId, "RoleChanged", oldRole, request.Role), cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var members = await _groups.GetMembersAsync(groupId, cancellationToken);
            return members.First(m => m.UserId == memberUserId);
        }

        public async Task RemoveMemberAsync(Guid userId, Guid groupId, Guid memberUserId, CancellationToken cancellationToken = default)
        {
            var access = await _access.GetAccessAsync(groupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();

            await RemoveCoreAsync(groupId, userId, memberUserId, memberUserId == userId ? "Left" : "Removed", cancellationToken);
        }

        public async Task LeaveGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default)
        {
            await _access.GetAccessAsync(groupId, userId, cancellationToken);
            await RemoveCoreAsync(groupId, userId, userId, "Left", cancellationToken);
        }

        public async Task<IReadOnlyList<GroupActivityDto>> GetActivityAsync(
            Guid userId, Guid groupId, int skip, int take, CancellationToken cancellationToken = default)
        {
            var access = await _access.GetAccessAsync(groupId, userId, cancellationToken);
            access.RequireCanWork();

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

        private async Task<GroupDto> SetStatusAsync(Guid userId, Guid groupId, string newStatus, CancellationToken cancellationToken)
        {
            var access = await _access.GetAccessAsync(groupId, userId, cancellationToken);
            access.RequireLeader();

            if (access.Group.Status == newStatus)
            {
                throw new UserFriendlyException(newStatus == GroupStatuses.Archived
                    ? "Nhóm đã được lưu trữ rồi."
                    : "Nhóm đang hoạt động, không cần khôi phục.");
            }

            var oldStatus = access.Group.Status;
            access.Group.Status = newStatus;
            await _work.AddActivityAsync(GroupRules.Log(groupId, userId, GroupEntityTypes.Group, groupId,
                newStatus == GroupStatuses.Archived ? "Archived" : "Restored", oldStatus, newStatus), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(access.Group, access.Role, await _groups.CountMembersAsync(groupId, cancellationToken));
        }

        // Dùng chung cho Leader xóa thành viên và cho việc tự rời nhóm
        private async Task RemoveCoreAsync(Guid groupId, Guid actorId, Guid memberUserId, string action, CancellationToken cancellationToken)
        {
            var member = await _groups.GetMemberAsync(groupId, memberUserId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy thành viên trong nhóm.");

            if (member.Role == GroupRoles.Leader && await _groups.CountLeadersAsync(groupId, cancellationToken) <= 1)
            {
                throw new UserFriendlyException("Bạn là Leader cuối cùng của nhóm. Hãy chọn Leader khác trước khi rời hoặc xóa.");
            }

            var cleared = await _groups.ClearAssignmentsAsync(groupId, memberUserId, cancellationToken);

            // Phiên bấm giờ đang chạy của người đó trong nhóm này được dừng, nếu không nó treo mãi và chặn họ bấm giờ ở nhóm khác
            var running = await _work.GetRunningFocusSessionAsync(memberUserId, cancellationToken);
            if (running is not null && running.GroupTask.GroupId == groupId)
            {
                running.EndedAt = DateTime.UtcNow;
                running.StopReason = FocusStopReason.Other.ToString();
                running.Note = "Tự dừng vì người làm không còn trong nhóm.";
            }

            _groups.RemoveMember(member);
            await _work.AddActivityAsync(GroupRules.Log(groupId, actorId, GroupEntityTypes.Member, memberUserId, action,
                member.Role, cleared > 0 ? $"{cleared} việc về chưa giao" : null), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private static void ValidateRequest(GroupRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength)
            {
                throw new UserFriendlyException($"Tên nhóm không được để trống và tối đa {MaxNameLength} ký tự.");
            }

            if (request.Description is { Length: > MaxDescriptionLength })
            {
                throw new UserFriendlyException($"Mô tả nhóm tối đa {MaxDescriptionLength} ký tự.");
            }

            if (request.StartDate is { } start && request.Deadline is { } deadline && deadline < start)
            {
                throw new UserFriendlyException("Deadline không được trước ngày bắt đầu.");
            }
        }

        private static void ApplyRequest(WorkGroup group, GroupRequestDto request)
        {
            group.Name = request.Name.Trim();
            group.Description = request.Description;
            group.StartDate = request.StartDate;
            group.Deadline = request.Deadline;
        }

        private static GroupDto ToDto(WorkGroup group, string myRole, int memberCount)
        {
            return new GroupDto
            {
                Id = group.Id,
                Name = group.Name,
                Description = group.Description,
                OwnerId = group.OwnerId,
                StartDate = group.StartDate,
                Deadline = group.Deadline,
                Status = group.Status,
                CreatedAt = group.CreatedAt,
                MyRole = myRole,
                MemberCount = memberCount
            };
        }
    }
}
