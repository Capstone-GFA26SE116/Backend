using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IProjectDocumentService
    {
        Task<IReadOnlyList<ProjectDocumentDto>> GetDocumentsAsync(Guid userId, Guid projectId, string? search, string? tag, CancellationToken cancellationToken = default);

        // Mọi tài liệu của một người dùng qua tất cả dự án của họ. projectId, search (tên), tag, sourceType (File hoặc Link) là tùy chọn
        Task<IReadOnlyList<ProjectDocumentListItemDto>> GetUserDocumentsAsync(Guid userId, Guid? projectId, string? search, string? tag, string? sourceType, CancellationToken cancellationToken = default);

        // Tài liệu của mọi người dùng, có phân trang (dành cho Admin). userId để trống là tất cả
        Task<PagedResultDto<ProjectDocumentListItemDto>> GetAllDocumentsAsync(Guid? userId, Guid? projectId, string? search, string? tag, string? sourceType, int page, int pageSize, CancellationToken cancellationToken = default);

        Task<ProjectDocumentDto> GetDocumentAsync(Guid userId, Guid documentId, CancellationToken cancellationToken = default);

        Task<ProjectDocumentDto> CreateFileDocumentAsync(Guid userId, Guid projectId, string? name, string? tag, string originalFileName, long sizeBytes, Stream content, CancellationToken cancellationToken = default);

        Task<ProjectDocumentDto> CreateLinkDocumentAsync(Guid userId, Guid projectId, ProjectDocumentRequestDto request, CancellationToken cancellationToken = default);

        Task<ProjectDocumentDto> UpdateDocumentAsync(Guid userId, Guid documentId, ProjectDocumentRequestDto request, CancellationToken cancellationToken = default);

        Task DeleteDocumentAsync(Guid userId, Guid documentId, CancellationToken cancellationToken = default);

        Task<DocumentDownload> OpenDownloadAsync(Guid userId, Guid documentId, CancellationToken cancellationToken = default);
    }
}
