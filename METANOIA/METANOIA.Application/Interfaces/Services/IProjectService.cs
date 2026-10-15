using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IProjectService
    {
        // Mọi dự án của user (có hoặc không có khách hàng); dự án có khách hàng thì kèm thông tin khách hàng
        Task<IReadOnlyList<ProjectWithClientDto>> GetProjectsAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ProjectWithClientDto>> GetProjectsByUserIdAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken = default);

        // Lọc dự án của user theo name, status, clientId. Mỗi tham số để trống thì không lọc theo nó (kể cả dự án đã lưu trữ)
        Task<IReadOnlyList<ProjectWithClientDto>> FilterProjectsAsync(
            Guid userId, string? name, string? status, Guid? clientId, CancellationToken cancellationToken = default);

        Task<ProjectDto> GetProjectAsync(Guid userId, Guid projectId, CancellationToken cancellationToken = default);

        Task<ProjectDto> CreateProjectAsync(Guid userId, ProjectRequestDto request, CancellationToken cancellationToken = default);

        Task<ProjectDto> UpdateProjectAsync(Guid userId, Guid projectId, ProjectRequestDto request, CancellationToken cancellationToken = default);

        Task<ProjectDto> ArchiveProjectAsync(Guid userId, Guid projectId, CancellationToken cancellationToken = default);

        Task<ProjectDto> RestoreProjectAsync(Guid userId, Guid projectId, CancellationToken cancellationToken = default);

        Task<ProjectProgressDto> GetProgressAsync(Guid userId, Guid projectId, CancellationToken cancellationToken = default);
    }
}
