using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IProjectDocumentRepository
    {
        Task<bool> ProjectOwnedByUserAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ProjectDocument>> GetByProjectIdAsync(Guid projectId, string? search, string? tag, CancellationToken cancellationToken = default);

        Task<ProjectDocument?> GetByIdForUserAsync(Guid documentId, Guid userId, CancellationToken cancellationToken = default);

        Task AddAsync(ProjectDocument document, CancellationToken cancellationToken = default);

        void Remove(ProjectDocument document);
    }
}
