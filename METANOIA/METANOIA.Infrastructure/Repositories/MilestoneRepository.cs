using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class MilestoneRepository : IMilestoneRepository
    {
        private readonly AppDbContext _context;

        public MilestoneRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<bool> ProjectOwnedByUserAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Projects.AnyAsync(p => p.Id == projectId && p.UserId == userId, cancellationToken);
        }

        public async Task<IReadOnlyList<Milestone>> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            return await _context.Milestones
                .Where(m => m.ProjectId == projectId)
                .OrderBy(m => m.OrderIndex)
                .ToListAsync(cancellationToken);
        }

        public Task<Milestone?> GetByIdForUserAsync(Guid milestoneId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Milestones
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.Project.UserId == userId, cancellationToken);
        }

        public Task<bool> OrderIndexTakenAsync(Guid projectId, int orderIndex, Guid excludeId, CancellationToken cancellationToken = default)
        {
            return _context.Milestones.AnyAsync(
                m => m.ProjectId == projectId && m.OrderIndex == orderIndex && m.Id != excludeId,
                cancellationToken);
        }

        public async Task AddAsync(Milestone milestone, CancellationToken cancellationToken = default)
        {
            await _context.Milestones.AddAsync(milestone, cancellationToken);
        }

        public void Remove(Milestone milestone)
        {
            _context.Milestones.Remove(milestone);
        }
    }
}
