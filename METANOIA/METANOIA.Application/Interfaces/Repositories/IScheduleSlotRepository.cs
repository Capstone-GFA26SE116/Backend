using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IScheduleSlotRepository
    {
        Task<bool> TaskOwnedByUserAsync(Guid taskId, Guid userId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ScheduleSlot>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default);

        Task<ScheduleSlot?> GetByIdForUserAsync(Guid slotId, Guid userId, CancellationToken cancellationToken = default);

        Task<bool> SubTaskBelongsToTaskAsync(Guid subTaskId, Guid taskId, CancellationToken cancellationToken = default);

        Task<bool> ProposalOwnedByUserAsync(Guid proposalId, Guid userId, CancellationToken cancellationToken = default);

        Task AddAsync(ScheduleSlot slot, CancellationToken cancellationToken = default);
        Task<ScheduleSlot?> GetByIdAsync(Guid slotId);

        // Trạng thái của task (để không xếp lịch cho task đã Done hoặc Skipped)
        Task<string?> GetTaskStatusAsync(Guid taskId, CancellationToken cancellationToken = default);
        Task<ScheduleSlot?> GetSlotWithSessionsAsync(Guid slotId, Guid userId);

        // Slot cần đẩy lên Google Calendar: đang sống (liveStatuses), hoặc đã đẩy (có GoogleEventId) mà nay ở removedStatuses để xóa sự kiện.
        // Lọc theo StartTime trong [fromUtc, toUtc) nếu có. Kèm Task, Project, SubTask để dựng tiêu đề; các thực thể được theo dõi để lưu GoogleEventId
        Task<IReadOnlyList<ScheduleSlot>> GetForGooglePushAsync(
            Guid userId, DateTime? fromUtc, DateTime? toUtc,
            IReadOnlyCollection<string> liveStatuses, IReadOnlyCollection<string> removedStatuses,
            CancellationToken cancellationToken = default);

        // Slot đang chiếm giờ của người dùng (Approved, Due, InProgress, Overrun) giao với [startUtc, endUtc) (chạm đầu mút không tính),
        // trừ slot excludeSlotId. Kèm Task để biết tiêu đề. Không theo dõi
        Task<IReadOnlyList<ScheduleSlot>> GetLiveSlotsOverlappingAsync(
            Guid userId, DateTime startUtc, DateTime endUtc, Guid? excludeSlotId, CancellationToken cancellationToken = default);

        // ---- Slot nhóm
        // Slot của một việc nhóm (và các việc con), kèm người xếp, việc và việc con. Không theo dõi
        Task<IReadOnlyList<ScheduleSlot>> GetByGroupTaskIdAsync(Guid groupTaskId, bool includeCancelled, CancellationToken cancellationToken = default);

        // Mọi slot của một việc nhóm kèm lịch sử đổi giờ, theo dõi để xóa khi xóa việc
        Task<IReadOnlyList<ScheduleSlot>> GetSlotsWithLogsOfGroupTaskAsync(Guid groupTaskId, CancellationToken cancellationToken = default);

        void Remove(ScheduleSlot slot);

        // Slot nhóm chưa xong (Proposed, Approved, Due, InProgress, Overrun) của một việc, theo dõi để sửa; kèm việc, việc con và nhóm
        Task<IReadOnlyList<ScheduleSlot>> GetOpenSlotsOfGroupTaskAsync(Guid groupTaskId, CancellationToken cancellationToken = default);

        // Slot nhóm chưa xong của một người trong một nhóm, theo dõi để sửa
        Task<IReadOnlyList<ScheduleSlot>> GetOpenSlotsOfUserInGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default);

        // Slot Approved chưa tới giờ của người dùng (hẹn lại nhắc việc khi họ đổi số phút nhắc trước)
        Task<IReadOnlyList<ScheduleSlot>> GetUpcomingApprovedByUserAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken = default);
    }
}
