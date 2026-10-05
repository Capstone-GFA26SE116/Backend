using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IProjectService
    {
        Task<IReadOnlyList<ProjectDto>> GetProjectsAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken = default);

        Task<ProjectDto> GetProjectAsync(Guid userId, Guid projectId, CancellationToken cancellationToken = default);

        Task<ProjectDto> CreateProjectAsync(Guid userId, ProjectRequestDto request, CancellationToken cancellationToken = default);

        Task<ProjectDto> UpdateProjectAsync(Guid userId, Guid projectId, ProjectRequestDto request, CancellationToken cancellationToken = default);

        Task<ProjectDto> ArchiveProjectAsync(Guid userId, Guid projectId, CancellationToken cancellationToken = default);

        Task<ProjectDto> RestoreProjectAsync(Guid userId, Guid projectId, CancellationToken cancellationToken = default);

        Task<ProjectProgressDto> GetProgressAsync(Guid userId, Guid projectId, CancellationToken cancellationToken = default);
    }
}
