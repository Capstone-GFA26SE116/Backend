using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class ProjectRepository : IProjectRepository
    {
        private const string ArchivedStatus = "Archived";

        private readonly AppDbContext _context;

        public ProjectRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<Project>> GetByUserIdAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken = default)
        {
            return await _context.Projects
                .Where(p => p.UserId == userId && (includeArchived || p.Status != ArchivedStatus))
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public Task<Project?> GetByIdForUserAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Projects
                .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, cancellationToken);
        }

        public Task<Project?> GetWithProgressDataAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Projects
                .Include(p => p.Tasks).ThenInclude(t => t.SubTasks)
                .Include(p => p.Tasks).ThenInclude(t => t.FocusSessions)
                .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, cancellationToken);
        }

        public Task<bool> ClientBelongsToUserAsync(Guid clientId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Clients.AnyAsync(c => c.Id == clientId && c.UserId == userId, cancellationToken);
        }

        public async Task AddAsync(Project project, CancellationToken cancellationToken = default)
        {
            await _context.Projects.AddAsync(project, cancellationToken);
        }
    }
}
