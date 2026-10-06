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
    }
}
