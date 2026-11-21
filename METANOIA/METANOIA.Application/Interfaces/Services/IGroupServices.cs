using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Services
{
    // Quyền của một người trong một nhóm. Người không thuộc nhóm không bao giờ có GroupAccess (nhận 404 từ IGroupAccessService).
    public sealed record GroupAccess(WorkGroup Group, Guid UserId, string Role)
    {
        public bool IsLeader => Role == GroupRoles.Leader;

        // Leader và Member làm việc được; Viewer chỉ xem
        public bool CanWork => Role is GroupRoles.Leader or GroupRoles.Member;

        public void RequireLeader()
        {
            if (!IsLeader)
            {
                throw new GroupForbiddenException("Chỉ Leader của nhóm mới được làm việc này.");
            }
        }

        public void RequireCanWork()
        {
            if (!CanWork)
            {
                throw new GroupForbiddenException("Viewer chỉ được xem, không được thao tác.");
            }
        }

        // Nhóm đã lưu trữ chỉ xem được
        public void RequireActive()
        {
            if (Group.Status == GroupStatuses.Archived)
            {
                throw new UserFriendlyException("Nhóm đã được lưu trữ, chỉ xem được. Hãy khôi phục nhóm trước.");
            }
        }
    }

    // Một chỗ kiểm quyền duy nhất của nhánh Group
    public interface IGroupAccessService
    {
        // Nhóm không tồn tại hoặc người gọi không thuộc nhóm: NotFoundException (404)
        Task<GroupAccess> GetAccessAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);
    }

    // Gửi thông báo nhóm qua hộp thư Notification, SignalR và Web Push. Lỗi gửi chỉ được ghi log, không làm hỏng việc đang làm.
    public interface IGroupNotifier
    {
        // groupId (và groupTaskId nếu liên quan tới một việc) được lưu cùng thông báo để FE mở thẳng nhóm hoặc việc
        Task NotifyAsync(Guid userId, string type, string title, string message, Guid groupId, Guid? groupTaskId = null);
    }

    public interface IGroupService
    {
        Task<IReadOnlyList<GroupDto>> GetMyGroupsAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken = default);

        Task<GroupDto> GetGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default);

        // Người tạo thành Leader đầu tiên
        Task<GroupDto> CreateGroupAsync(Guid userId, GroupRequestDto request, CancellationToken cancellationToken = default);

        Task<GroupDto> UpdateGroupAsync(Guid userId, Guid groupId, GroupRequestDto request, CancellationToken cancellationToken = default);

        Task<GroupDto> ArchiveGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default);

        Task<GroupDto> RestoreGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GroupMemberDto>> GetMembersAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default);

        Task<GroupMemberDto> ChangeMemberRoleAsync(Guid userId, Guid groupId, Guid memberUserId, GroupMemberRoleRequestDto request, CancellationToken cancellationToken = default);

        // Leader xóa một thành viên; việc đang giao cho người đó về chưa giao
        Task RemoveMemberAsync(Guid userId, Guid groupId, Guid memberUserId, CancellationToken cancellationToken = default);

        Task LeaveGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default);

        // Nhật ký hoạt động, mới nhất trước (Leader và Member xem được)
        Task<IReadOnlyList<GroupActivityDto>> GetActivityAsync(Guid userId, Guid groupId, int skip, int take, CancellationToken cancellationToken = default);
    }

    public interface IGroupInvitationService
    {
        Task<GroupInvitationCreatedDto> CreateInvitationAsync(Guid userId, Guid groupId, GroupInvitationRequestDto request, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GroupInvitationDto>> GetInvitationsOfGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default);

        Task RevokeInvitationAsync(Guid userId, Guid groupId, Guid invitationId, CancellationToken cancellationToken = default);

        // Lời mời đang chờ gửi tới email của người gọi
        Task<IReadOnlyList<GroupInvitationDto>> GetMyInvitationsAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<GroupDto> AcceptInvitationAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken = default);

        Task<GroupDto> AcceptInvitationByTokenAsync(Guid userId, string token, CancellationToken cancellationToken = default);

        Task DeclineInvitationAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken = default);
    }

    public interface IGroupMilestoneService
    {
        Task<IReadOnlyList<GroupMilestoneDto>> GetMilestonesAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default);

        Task<GroupMilestoneDto> GetMilestoneAsync(Guid userId, Guid milestoneId, CancellationToken cancellationToken = default);

        Task<GroupMilestoneDto> CreateMilestoneAsync(Guid userId, Guid groupId, GroupMilestoneRequestDto request, CancellationToken cancellationToken = default);

        Task<GroupMilestoneDto> UpdateMilestoneAsync(Guid userId, Guid milestoneId, GroupMilestoneRequestDto request, CancellationToken cancellationToken = default);

        Task DeleteMilestoneAsync(Guid userId, Guid milestoneId, CancellationToken cancellationToken = default);
    }

    public interface IGroupTaskService
    {
        // ---- Việc
        Task<IReadOnlyList<GroupTaskDto>> GetTasksAsync(Guid userId, Guid groupId, string? status, Guid? assigneeId, Guid? milestoneId, CancellationToken cancellationToken = default);

        Task<GroupTaskDto> GetTaskAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default);

        Task<GroupTaskDto> CreateTaskAsync(Guid userId, Guid groupId, GroupTaskRequestDto request, CancellationToken cancellationToken = default);

        Task<GroupTaskDto> UpdateTaskAsync(Guid userId, Guid taskId, GroupTaskRequestDto request, CancellationToken cancellationToken = default);

        Task DeleteTaskAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default);

        Task<GroupTaskDto> ChangeTaskStatusAsync(Guid userId, Guid taskId, GroupTaskStatusRequestDto request, CancellationToken cancellationToken = default);

        Task<GroupTaskDto> AssignTaskAsync(Guid userId, Guid taskId, GroupAssigneeRequestDto request, CancellationToken cancellationToken = default);

        // ---- Việc con
        Task<IReadOnlyList<GroupSubTaskDto>> GetSubTasksAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default);

        Task<GroupSubTaskDto> GetSubTaskAsync(Guid userId, Guid subTaskId, CancellationToken cancellationToken = default);

        Task<GroupSubTaskDto> CreateSubTaskAsync(Guid userId, Guid taskId, GroupSubTaskRequestDto request, CancellationToken cancellationToken = default);

        Task<GroupSubTaskDto> UpdateSubTaskAsync(Guid userId, Guid subTaskId, GroupSubTaskRequestDto request, CancellationToken cancellationToken = default);

        Task DeleteSubTaskAsync(Guid userId, Guid subTaskId, CancellationToken cancellationToken = default);

        Task<GroupSubTaskDto> ChangeSubTaskStatusAsync(Guid userId, Guid subTaskId, GroupTaskStatusRequestDto request, CancellationToken cancellationToken = default);

        Task<GroupSubTaskDto> AssignSubTaskAsync(Guid userId, Guid subTaskId, GroupAssigneeRequestDto request, CancellationToken cancellationToken = default);
    }

    public interface IGroupCommentService
    {
        Task<IReadOnlyList<GroupCommentDto>> GetCommentsAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default);

        Task<GroupCommentDto> CreateCommentAsync(Guid userId, Guid taskId, GroupCommentRequestDto request, CancellationToken cancellationToken = default);

        // Chỉ tác giả sửa được; tác giả hoặc Leader xóa được
        Task<GroupCommentDto> UpdateCommentAsync(Guid userId, Guid commentId, GroupCommentRequestDto request, CancellationToken cancellationToken = default);

        Task DeleteCommentAsync(Guid userId, Guid commentId, CancellationToken cancellationToken = default);
    }

    public interface IGroupProgressService
    {
        Task<GroupProgressDto> GetProgressAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default);

        // Việc và việc con được giao cho người gọi, qua mọi nhóm đang hoạt động
        Task<MyGroupWorkDto> GetMyWorkAsync(Guid userId, CancellationToken cancellationToken = default);
    }

    // ---------------------------------------------------------------- Đợt 2

    // Bấm giờ làm việc trên việc của nhóm. Chỉ người được giao (việc hoặc việc con) bấm được; giờ làm ghi theo từng người.
    public interface IGroupFocusService
    {
        // Việc có việc con thì phải chọn việc con. Đang có phiên chạy (của nhóm hoặc cá nhân): FocusConflictException (409)
        Task<GroupFocusStartResponseDto> StartAsync(Guid userId, Guid taskId, GroupFocusStartRequestDto request, CancellationToken cancellationToken = default);

        // Chỉ người bấm Start dừng được phiên của mình. Dừng Completed thì việc con (hoặc việc không có việc con) thành Done
        Task<GroupFocusStopResponseDto> StopAsync(Guid userId, Guid sessionId, GroupFocusStopRequestDto request, CancellationToken cancellationToken = default);

        // Phiên đang chạy của người gọi; null nếu không có
        Task<GroupFocusCurrentDto?> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default);

        // Giờ làm thực tế của một việc theo từng người và từng việc con (mọi thành viên xem được)
        Task<GroupTaskTimeDto> GetTaskTimeAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default);
    }

    // Kiểm tra lịch của người nhận việc: chỉ trả bận hoặc rảnh, không lộ tên dự án hay tiêu đề lịch cá nhân
    public interface IGroupAvailabilityService
    {
        Task<GroupAssigneeAvailabilityDto> CheckAsync(
            Guid assigneeId,
            DateTime? fixedStartUtc,
            DateTime? fixedEndUtc,
            DateTime? deadlineUtc,
            int? estimatedMinutes,
            Guid? excludeTaskId,
            CancellationToken cancellationToken = default);

        // Leader xem trước trước khi giao: người nhận có bận vào giờ của việc này không
        Task<GroupAssigneeAvailabilityDto> CheckForTaskAsync(Guid userId, Guid taskId, Guid assigneeId, CancellationToken cancellationToken = default);

        // Ném GroupAssigneeBusyException (409) nếu người nhận bận hoặc quá tải và confirm = false
        Task EnsureAvailableAsync(
            Guid assigneeId,
            DateTime? fixedStartUtc,
            DateTime? fixedEndUtc,
            DateTime? deadlineUtc,
            int? estimatedMinutes,
            Guid? excludeTaskId,
            bool confirm,
            CancellationToken cancellationToken = default);
    }

    public interface IGroupChangeRequestService
    {
        // Người được giao việc (hoặc một việc con của việc) đề xuất đổi giờ cố định hoặc deadline. Leader nhận thông báo
        Task<GroupChangeRequestDto> CreateAsync(Guid userId, Guid taskId, GroupChangeRequestCreateDto request, CancellationToken cancellationToken = default);

        // Leader thấy mọi đề xuất; Member chỉ thấy đề xuất của mình; Viewer không xem
        Task<IReadOnlyList<GroupChangeRequestDto>> GetOfGroupAsync(Guid userId, Guid groupId, string? status, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GroupChangeRequestDto>> GetOfTaskAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default);

        // Leader duyệt: giá trị mới được áp dụng vào việc (kiểm tra lịch người nhận, 409 nếu trùng trừ khi ConfirmConflicts)
        Task<GroupChangeRequestDto> ApproveAsync(Guid userId, Guid requestId, GroupChangeDecisionDto request, CancellationToken cancellationToken = default);

        // Leader từ chối, bắt buộc có lý do
        Task<GroupChangeRequestDto> RejectAsync(Guid userId, Guid requestId, GroupChangeDecisionDto request, CancellationToken cancellationToken = default);

        // Người đề xuất tự rút lại đề xuất đang chờ
        Task<GroupChangeRequestDto> CancelAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default);
    }
}
