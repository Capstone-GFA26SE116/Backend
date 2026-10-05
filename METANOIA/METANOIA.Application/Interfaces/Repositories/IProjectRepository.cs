using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IProjectRepository
    {
        Task<IReadOnlyList<Project>> GetByUserIdAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken = default);

        Task<Project?> GetByIdForUserAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default);

        Task<Project?> GetWithProgressDataAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default);

        Task<bool> ClientBelongsToUserAsync(Guid clientId, Guid userId, CancellationToken cancellationToken = default);

        Task AddAsync(Project project, CancellationToken cancellationToken = default);
    }
}
