using METANOIA.Application.Dto;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    // Một dòng của danh sách "nhóm của tôi": nhóm, vai trò của người gọi và số thành viên
    public sealed record GroupSummaryRow(WorkGroup Group, string Role, int MemberCount);

    // Nhóm, thành viên, lời mời
    public interface IGroupRepository
    {
        Task<WorkGroup?> GetGroupAsync(Guid groupId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GroupSummaryRow>> GetGroupsOfUserAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken = default);

        Task AddGroupAsync(WorkGroup group, CancellationToken cancellationToken = default);

        // Dòng thành viên (theo dõi để sửa); null nếu người đó không thuộc nhóm
        Task<GroupMember?> GetMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GroupMemberDto>> GetMembersAsync(Guid groupId, CancellationToken cancellationToken = default);

        Task<int> CountMembersAsync(Guid groupId, CancellationToken cancellationToken = default);

        Task<int> CountLeadersAsync(Guid groupId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Guid>> GetLeaderIdsAsync(Guid groupId, CancellationToken cancellationToken = default);

        Task AddMemberAsync(GroupMember member, CancellationToken cancellationToken = default);

        void RemoveMember(GroupMember member);

        // Việc và việc con của nhóm đang giao cho người này về chưa giao; trả về số dòng đã đổi
        Task<int> ClearAssignmentsAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);

        Task AddInvitationAsync(GroupInvitation invitation, CancellationToken cancellationToken = default);

        // Kèm nhóm và người mời
        Task<GroupInvitation?> GetInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default);

        Task<GroupInvitation?> GetInvitationByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GroupInvitation>> GetInvitationsOfGroupAsync(Guid groupId, CancellationToken cancellationToken = default);

        // Lời mời còn chờ (chưa trả lời, chưa hết hạn) gửi tới email này
        Task<IReadOnlyList<GroupInvitation>> GetPendingInvitationsForEmailAsync(string email, DateTime nowUtc, CancellationToken cancellationToken = default);

        // Lời mời đang chờ của email trong nhóm (kể cả đã quá hạn nhưng chưa được đánh dấu Expired)
        Task<GroupInvitation?> GetPendingInvitationAsync(Guid groupId, string email, CancellationToken cancellationToken = default);
    }

    // Giai đoạn, việc, việc con, bình luận, nhật ký và các truy vấn tiến độ
    public interface IGroupWorkRepository
    {
        // ---- Giai đoạn
        Task<IReadOnlyList<GroupMilestone>> GetMilestonesAsync(Guid groupId, CancellationToken cancellationToken = default);

        Task<GroupMilestone?> GetMilestoneAsync(Guid milestoneId, CancellationToken cancellationToken = default);

        Task<bool> MilestoneOrderIndexTakenAsync(Guid groupId, int orderIndex, Guid excludeId, CancellationToken cancellationToken = default);

        Task<bool> MilestoneBelongsToGroupAsync(Guid milestoneId, Guid groupId, CancellationToken cancellationToken = default);

        // Việc của giai đoạn chưa Done hoặc Skipped (cảnh báo khi giao giai đoạn còn việc dở)
        Task<IReadOnlyList<GroupOpenTaskDto>> GetOpenTasksAsync(Guid milestoneId, CancellationToken cancellationToken = default);

        Task AddMilestoneAsync(GroupMilestone milestone, CancellationToken cancellationToken = default);

        void RemoveMilestone(GroupMilestone milestone);

        // ---- Việc
        Task<IReadOnlyList<GroupTask>> GetTasksAsync(Guid groupId, string? status, Guid? assigneeId, Guid? milestoneId, CancellationToken cancellationToken = default);

        // Kèm việc con và người được giao (theo dõi để sửa)
        Task<GroupTask?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

        Task AddTaskAsync(GroupTask task, CancellationToken cancellationToken = default);

        void RemoveTask(GroupTask task);

        // ---- Việc con
        // Kèm việc cha (và các việc con của việc cha) để suy lại trạng thái
        Task<GroupSubTask?> GetSubTaskAsync(Guid subTaskId, CancellationToken cancellationToken = default);

        Task<bool> SubTaskOrderIndexTakenAsync(Guid taskId, int orderIndex, Guid excludeId, CancellationToken cancellationToken = default);

        Task<int> NextSubTaskOrderIndexAsync(Guid taskId, CancellationToken cancellationToken = default);

        Task AddSubTaskAsync(GroupSubTask subTask, CancellationToken cancellationToken = default);

        void RemoveSubTask(GroupSubTask subTask);

        // ---- Bình luận
        Task<IReadOnlyList<GroupTaskComment>> GetCommentsAsync(Guid taskId, CancellationToken cancellationToken = default);

        Task<GroupTaskComment?> GetCommentAsync(Guid commentId, CancellationToken cancellationToken = default);

        Task AddCommentAsync(GroupTaskComment comment, CancellationToken cancellationToken = default);

        void RemoveComment(GroupTaskComment comment);

        // ---- Nhật ký
        Task AddActivityAsync(GroupActivityLog log, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GroupActivityLog>> GetActivityAsync(Guid groupId, int skip, int take, CancellationToken cancellationToken = default);

        // ---- Người dùng
        Task<Dictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default);

        // ---- Tiến độ và việc của tôi
        // Mọi việc của nhóm kèm việc con (không theo dõi)
        Task<IReadOnlyList<GroupTask>> GetTasksForProgressAsync(Guid groupId, CancellationToken cancellationToken = default);

        // Việc giao cho người này trong các nhóm đang hoạt động, kèm nhóm
        Task<IReadOnlyList<GroupTask>> GetTasksAssignedToAsync(Guid userId, CancellationToken cancellationToken = default);

        // Việc con giao cho người này trong các nhóm đang hoạt động, kèm việc cha và nhóm
        Task<IReadOnlyList<GroupSubTask>> GetSubTasksAssignedToAsync(Guid userId, CancellationToken cancellationToken = default);

        // ---- Phiên bấm giờ (đợt 2)
        Task AddFocusSessionAsync(GroupFocusSession session, CancellationToken cancellationToken = default);

        // Theo dõi để sửa; kèm việc (và các việc con của việc) cùng việc con của phiên
        Task<GroupFocusSession?> GetFocusSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

        // Phiên đang chạy của người này (EndedAt rỗng), kèm việc, nhóm và việc con; null nếu không có
        Task<GroupFocusSession?> GetRunningFocusSessionAsync(Guid userId, CancellationToken cancellationToken = default);

        // Mọi phiên của một việc (kể cả đang chạy), kèm người làm, cũ trước
        Task<IReadOnlyList<GroupFocusSession>> GetFocusSessionsOfTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

        // Các phiên đã kết thúc của mọi việc trong nhóm (không theo dõi), để tính giờ làm thực tế
        Task<IReadOnlyList<GroupFocusSession>> GetFinishedFocusSessionsOfGroupAsync(Guid groupId, CancellationToken cancellationToken = default);

        // ---- Đề xuất đổi giờ (đợt 2)
        Task AddChangeRequestAsync(GroupTaskChangeRequest request, CancellationToken cancellationToken = default);

        // Theo dõi để sửa; kèm việc (và người được giao, các việc con), người đề xuất, người quyết định
        Task<GroupTaskChangeRequest?> GetChangeRequestAsync(Guid requestId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GroupTaskChangeRequest>> GetChangeRequestsOfGroupAsync(Guid groupId, string? status, Guid? requestedById, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GroupTaskChangeRequest>> GetChangeRequestsOfTaskAsync(Guid taskId, Guid? requestedById, CancellationToken cancellationToken = default);

        Task<GroupTaskChangeRequest?> GetPendingChangeRequestAsync(Guid taskId, Guid requestedById, CancellationToken cancellationToken = default);

        // ---- Kiểm tra lịch người nhận việc (đợt 2)
        // Việc nhóm chưa xong, có giờ cố định giao với [startUtc, endUtc), được giao cho người này, ở các nhóm đang hoạt động
        Task<IReadOnlyList<GroupTask>> GetFixedTasksOverlappingAsync(Guid userId, DateTime startUtc, DateTime endUtc, Guid? excludeTaskId, CancellationToken cancellationToken = default);

        // Tổng phút ước lượng của các việc nhóm chưa xong giao cho người này có deadline trong [fromUtc, toUtc]
        Task<int> SumOpenEstimatedMinutesAsync(Guid userId, DateTime fromUtc, DateTime toUtc, Guid? excludeTaskId, CancellationToken cancellationToken = default);
    }
}
