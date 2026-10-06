using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IProjectDocumentService
    {
        Task<IReadOnlyList<ProjectDocumentDto>> GetDocumentsAsync(Guid userId, Guid projectId, string? search, string? tag, CancellationToken cancellationToken = default);

        Task<ProjectDocumentDto> GetDocumentAsync(Guid userId, Guid documentId, CancellationToken cancellationToken = default);

        Task<ProjectDocumentDto> CreateFileDocumentAsync(Guid userId, Guid projectId, string? name, string? tag, string originalFileName, long sizeBytes, Stream content, CancellationToken cancellationToken = default);

        Task<ProjectDocumentDto> CreateLinkDocumentAsync(Guid userId, Guid projectId, ProjectDocumentRequestDto request, CancellationToken cancellationToken = default);

        Task<ProjectDocumentDto> UpdateDocumentAsync(Guid userId, Guid documentId, ProjectDocumentRequestDto request, CancellationToken cancellationToken = default);

        Task DeleteDocumentAsync(Guid userId, Guid documentId, CancellationToken cancellationToken = default);

        Task<DocumentDownload> OpenDownloadAsync(Guid userId, Guid documentId, CancellationToken cancellationToken = default);
    }
}
