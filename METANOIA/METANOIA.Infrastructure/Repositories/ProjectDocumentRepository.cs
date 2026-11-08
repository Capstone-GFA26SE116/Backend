using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class ProjectDocumentRepository : IProjectDocumentRepository
    {
        private readonly AppDbContext _context;

        public ProjectDocumentRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<bool> ProjectOwnedByUserAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Projects.AnyAsync(p => p.Id == projectId && p.UserId == userId, cancellationToken);
        }

        public async Task<IReadOnlyList<ProjectDocument>> GetByProjectIdAsync(Guid projectId, string? search, string? tag, CancellationToken cancellationToken = default)
        {
            var query = _context.ProjectDocuments.Where(d => d.ProjectId == projectId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = search.Trim().ToLower();
                query = query.Where(d => d.Name.ToLower().Contains(keyword));
            }

            if (tag is not null)
            {
                query = query.Where(d => d.Tag == tag);
            }

            return await query.OrderByDescending(d => d.UploadedAt).ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<ProjectDocumentListRow>> GetByUserAsync(
            Guid userId, Guid? projectId, string? search, string? tag, string? sourceType, CancellationToken cancellationToken = default)
        {
            var query = Filter(_context.ProjectDocuments.Where(d => d.Project.UserId == userId), projectId, search, tag, sourceType);

            return await ToRows(query).ToListAsync(cancellationToken);
        }

        public async Task<(IReadOnlyList<ProjectDocumentListRow> Items, int TotalCount)> SearchAllAsync(
            Guid? userId, Guid? projectId, string? search, string? tag, string? sourceType, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var query = _context.ProjectDocuments.AsQueryable();
            if (userId is { } owner)
            {
                query = query.Where(d => d.Project.UserId == owner);
            }

            query = Filter(query, projectId, search, tag, sourceType);

            var total = await query.CountAsync(cancellationToken);
            var items = await ToRows(query)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return (items, total);
        }

        // Bộ lọc dùng chung: dự án, tên (chứa chuỗi, không phân biệt hoa thường), tag, loại (File hoặc Link)
        private static IQueryable<ProjectDocument> Filter(
            IQueryable<ProjectDocument> query, Guid? projectId, string? search, string? tag, string? sourceType)
        {
            if (projectId is { } project)
            {
                query = query.Where(d => d.ProjectId == project);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = search.Trim().ToLower();
                query = query.Where(d => d.Name.ToLower().Contains(keyword));
            }

            if (tag is not null)
            {
                query = query.Where(d => d.Tag == tag);
            }

            if (sourceType is not null)
            {
                query = query.Where(d => d.SourceType == sourceType);
            }

            return query;
        }

        // Mới nhất trước; Id làm khóa phụ để thứ tự ổn định khi phân trang
        private static IQueryable<ProjectDocumentListRow> ToRows(IQueryable<ProjectDocument> query)
        {
            return query
                .OrderByDescending(d => d.UploadedAt)
                .ThenBy(d => d.Id)
                .Select(d => new ProjectDocumentListRow
                {
                    Document = d,
                    ProjectName = d.Project.Name,
                    UserId = d.Project.UserId
                });
        }

        public Task<ProjectDocument?> GetByIdForUserAsync(Guid documentId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.ProjectDocuments
                .FirstOrDefaultAsync(d => d.Id == documentId && d.Project.UserId == userId, cancellationToken);
        }

        public async Task AddAsync(ProjectDocument document, CancellationToken cancellationToken = default)
        {
            await _context.ProjectDocuments.AddAsync(document, cancellationToken);
        }

        public void Remove(ProjectDocument document)
        {
            _context.ProjectDocuments.Remove(document);
        }
    }
}
