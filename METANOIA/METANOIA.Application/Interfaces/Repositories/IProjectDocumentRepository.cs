using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    // Một dòng của danh sách tài liệu gom nhiều dự án
    public class ProjectDocumentListRow
    {
        public ProjectDocument Document { get; set; } = null!;

        public string ProjectName { get; set; } = null!;

        public Guid UserId { get; set; }
    }

    public interface IProjectDocumentRepository
    {
        Task<bool> ProjectOwnedByUserAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ProjectDocument>> GetByProjectIdAsync(Guid projectId, string? search, string? tag, CancellationToken cancellationToken = default);

        // Mọi tài liệu của một người dùng (qua tất cả dự án của họ). search lọc theo tên, projectId và sourceType (File hoặc Link) là tùy chọn
        Task<IReadOnlyList<ProjectDocumentListRow>> GetByUserAsync(Guid userId, Guid? projectId, string? search, string? tag, string? sourceType, CancellationToken cancellationToken = default);

        // Tài liệu của mọi người dùng, có phân trang. userId để trống là tất cả (dành cho Admin)
        Task<(IReadOnlyList<ProjectDocumentListRow> Items, int TotalCount)> SearchAllAsync(Guid? userId, Guid? projectId, string? search, string? tag, string? sourceType, int page, int pageSize, CancellationToken cancellationToken = default);

        Task<ProjectDocument?> GetByIdForUserAsync(Guid documentId, Guid userId, CancellationToken cancellationToken = default);

        Task AddAsync(ProjectDocument document, CancellationToken cancellationToken = default);

        void Remove(ProjectDocument document);
    }
}
