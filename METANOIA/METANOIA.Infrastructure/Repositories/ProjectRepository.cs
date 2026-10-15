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
            // Include trên khóa ngoại tùy chọn là LEFT JOIN: dự án không có khách hàng vẫn có mặt
            return await _context.Projects
                .Include(p => p.Client)
                .Where(p => p.UserId == userId && (includeArchived || p.Status != ArchivedStatus))
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Project>> GetProjectByUserIdAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken = default)
        {
            return await _context.Projects
                .Where(p => p.UserId == userId && (includeArchived || p.Status != ArchivedStatus))
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
        }



        public async Task<IReadOnlyList<Project>> SearchByUserAsync(
            Guid userId, string? name, string? status, Guid? clientId, CancellationToken cancellationToken = default)
        {
            var query = _context.Projects.Include(p => p.Client).Where(p => p.UserId == userId);

            if (!string.IsNullOrWhiteSpace(status))
            {
                var wanted = status.Trim();
                query = query.Where(p => p.Status == wanted);
            }

            if (clientId is { } client)
            {
                query = query.Where(p => p.ClientId == client);
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                var term = name.Trim().ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(term));
            }

            return await query.OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);
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
