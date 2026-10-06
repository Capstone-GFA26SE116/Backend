using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IScheduleChangeLogRepository
    {
        Task<IReadOnlyList<ScheduleChangeLog>> GetBySlotIdAsync(Guid slotId, CancellationToken cancellationToken = default);

        Task<ScheduleChangeLog?> GetByIdForUserAsync(Guid logId, Guid userId, CancellationToken cancellationToken = default);

        Task AddAsync(ScheduleChangeLog log, CancellationToken cancellationToken = default);
    }
}
