using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class ScheduleChangeLogRepository : IScheduleChangeLogRepository
    {
        private readonly AppDbContext _context;

        public ScheduleChangeLogRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<ScheduleChangeLog>> GetBySlotIdAsync(Guid slotId, CancellationToken cancellationToken = default)
        {
            return await _context.ScheduleChangeLogs
                .Where(l => l.ScheduleSlotId == slotId)
                .OrderByDescending(l => l.ChangedAt)
                .ToListAsync(cancellationToken);
        }

        public Task<ScheduleChangeLog?> GetByIdForUserAsync(Guid logId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.ScheduleChangeLogs
                .FirstOrDefaultAsync(l => l.Id == logId && l.ScheduleSlot.Task.Project.UserId == userId, cancellationToken);
        }

        public async Task AddAsync(ScheduleChangeLog log, CancellationToken cancellationToken = default)
        {
            await _context.ScheduleChangeLogs.AddAsync(log, cancellationToken);
        }
    }
}
