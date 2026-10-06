using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IMilestoneRepository
    {
        Task<bool> ProjectOwnedByUserAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Milestone>> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);

        Task<Milestone?> GetByIdForUserAsync(Guid milestoneId, Guid userId, CancellationToken cancellationToken = default);

        Task<bool> OrderIndexTakenAsync(Guid projectId, int orderIndex, Guid excludeId, CancellationToken cancellationToken = default);

        Task AddAsync(Milestone milestone, CancellationToken cancellationToken = default);

        void Remove(Milestone milestone);
    }
}
