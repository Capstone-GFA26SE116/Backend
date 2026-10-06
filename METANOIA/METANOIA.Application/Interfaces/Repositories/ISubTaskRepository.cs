using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface ISubTaskRepository
    {
        Task<bool> TaskOwnedByUserAsync(Guid taskId, Guid userId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SubTask>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default);

        Task<SubTask?> GetByIdForUserAsync(Guid subTaskId, Guid userId, CancellationToken cancellationToken = default);

        Task<bool> OrderIndexTakenAsync(Guid taskId, int orderIndex, Guid excludeId, CancellationToken cancellationToken = default);

        Task<bool> HasHistoryAsync(Guid subTaskId, CancellationToken cancellationToken = default);

        Task AddAsync(SubTask subTask, CancellationToken cancellationToken = default);

        void Remove(SubTask subTask);
    }
}
