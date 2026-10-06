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
