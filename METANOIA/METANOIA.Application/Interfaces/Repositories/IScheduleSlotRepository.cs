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
    }
}
