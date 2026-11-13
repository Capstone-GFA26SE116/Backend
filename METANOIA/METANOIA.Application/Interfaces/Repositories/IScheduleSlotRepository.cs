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

        // Slot Approved chưa tới giờ của người dùng (hẹn lại nhắc việc khi họ đổi số phút nhắc trước)
        Task<IReadOnlyList<ScheduleSlot>> GetUpcomingApprovedByUserAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken = default);
    }
}
