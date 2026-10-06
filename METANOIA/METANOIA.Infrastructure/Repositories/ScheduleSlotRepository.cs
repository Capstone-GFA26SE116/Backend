using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class ScheduleSlotRepository : IScheduleSlotRepository
    {
        private readonly AppDbContext _context;

        public ScheduleSlotRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<bool> TaskOwnedByUserAsync(Guid taskId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Tasks.AnyAsync(t => t.Id == taskId && t.Project.UserId == userId, cancellationToken);
        }

        public async Task<IReadOnlyList<ScheduleSlot>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            return await _context.ScheduleSlots
                .Where(s => s.TaskId == taskId)
                .OrderBy(s => s.StartTime)
                .ToListAsync(cancellationToken);
        }

        public Task<ScheduleSlot?> GetByIdForUserAsync(Guid slotId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.ScheduleSlots
                .FirstOrDefaultAsync(s => s.Id == slotId && s.Task.Project.UserId == userId, cancellationToken);
        }

        public Task<bool> SubTaskBelongsToTaskAsync(Guid subTaskId, Guid taskId, CancellationToken cancellationToken = default)
        {
            return _context.SubTasks.AnyAsync(s => s.Id == subTaskId && s.TaskId == taskId, cancellationToken);
        }

        public Task<bool> ProposalOwnedByUserAsync(Guid proposalId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.ScheduleProposals.AnyAsync(p => p.Id == proposalId && p.UserId == userId, cancellationToken);
        }

        public async Task AddAsync(ScheduleSlot slot, CancellationToken cancellationToken = default)
        {
            await _context.ScheduleSlots.AddAsync(slot, cancellationToken);
        }
    }
}
