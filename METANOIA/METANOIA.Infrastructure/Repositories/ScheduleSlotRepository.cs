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
                .Include(s => s.GroupTask)
                .OwnedBy(userId)
                .FirstOrDefaultAsync(s => s.Id == slotId, cancellationToken);
        }

        private static readonly string[] LiveStatuses = { "Approved", "Due", "InProgress", "Overrun" };

        public async Task<IReadOnlyList<ScheduleSlot>> GetLiveSlotsOverlappingAsync(
            Guid userId, DateTime startUtc, DateTime endUtc, Guid? excludeSlotId, CancellationToken cancellationToken = default)
        {
            return await _context.ScheduleSlots
                .AsNoTracking()
                .Include(s => s.Task)
                .Include(s => s.GroupTask)
                .OwnedBy(userId)
                .Where(s => LiveStatuses.Contains(s.Status)
                    && s.StartTime < endUtc
                    && s.EndTime > startUtc
                    && (excludeSlotId == null || s.Id != excludeSlotId))
                .OrderBy(s => s.StartTime)
                .ToListAsync(cancellationToken);
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

        public async Task<ScheduleSlot?> GetByIdAsync(Guid slotId)
        {
            return await _context.ScheduleSlots.FindAsync(slotId);
        }

        public async Task<IReadOnlyList<ScheduleSlot>> GetForGooglePushAsync(
            Guid userId, DateTime? fromUtc, DateTime? toUtc,
            IReadOnlyCollection<string> liveStatuses, IReadOnlyCollection<string> removedStatuses,
            CancellationToken cancellationToken = default)
        {
            var query = _context.ScheduleSlots
                .Include(s => s.Task).ThenInclude(t => t!.Project)
                .Include(s => s.SubTask)
                .Include(s => s.GroupTask).ThenInclude(t => t!.Group)
                .Include(s => s.GroupSubTask)
                .OwnedBy(userId)
                .Where(s => liveStatuses.Contains(s.Status) || (s.GoogleEventId != null && removedStatuses.Contains(s.Status)));

            if (fromUtc is { } from) query = query.Where(s => s.StartTime >= from);
            if (toUtc is { } to) query = query.Where(s => s.StartTime < to);

            return await query.OrderBy(s => s.StartTime).ToListAsync(cancellationToken);
        }

        // Slot nhóm "chưa xong": còn trên lịch hoặc đang làm (Proposed, Approved, Due, InProgress, Overrun)
        private static readonly string[] OpenGroupSlotStatuses = { "Proposed", "Approved", "Due", "InProgress", "Overrun" };

        public async Task<IReadOnlyList<ScheduleSlot>> GetByGroupTaskIdAsync(Guid groupTaskId, bool includeCancelled, CancellationToken cancellationToken = default)
        {
            var query = _context.ScheduleSlots
                .AsNoTracking()
                .Include(s => s.User)
                .Include(s => s.GroupTask)
                .Include(s => s.GroupSubTask)
                .Where(s => s.GroupTaskId == groupTaskId);

            if (!includeCancelled)
            {
                query = query.Where(s => s.Status != "Cancelled");
            }

            return await query.OrderBy(s => s.StartTime).ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<ScheduleSlot>> GetSlotsWithLogsOfGroupTaskAsync(Guid groupTaskId, CancellationToken cancellationToken = default)
        {
            return await _context.ScheduleSlots
                .Include(s => s.ScheduleChangeLogs)
                .Where(s => s.GroupTaskId == groupTaskId)
                .ToListAsync(cancellationToken);
        }

        public void Remove(ScheduleSlot slot)
        {
            _context.ScheduleSlots.Remove(slot);
        }

        public async Task<IReadOnlyList<ScheduleSlot>> GetOpenSlotsOfGroupTaskAsync(Guid groupTaskId, CancellationToken cancellationToken = default)
        {
            return await _context.ScheduleSlots
                .Include(s => s.GroupTask).ThenInclude(t => t!.Group)
                .Include(s => s.GroupSubTask)
                .Where(s => s.GroupTaskId == groupTaskId && OpenGroupSlotStatuses.Contains(s.Status))
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<ScheduleSlot>> GetOpenSlotsOfUserInGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default)
        {
            return await _context.ScheduleSlots
                .Include(s => s.GroupTask).ThenInclude(t => t!.Group)
                .Include(s => s.GroupSubTask)
                .Where(s => s.UserId == userId && s.GroupTask != null && s.GroupTask.GroupId == groupId && OpenGroupSlotStatuses.Contains(s.Status))
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<ScheduleSlot>> GetUpcomingApprovedByUserAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            return await _context.ScheduleSlots
                .OwnedBy(userId)
                .Where(s => s.Status == "Approved" && s.StartTime > nowUtc)
                .ToListAsync(cancellationToken);
        }

        public async Task<ScheduleSlot?> GetSlotWithSessionsAsync(Guid slotId, Guid userId)
        {
            return await _context.ScheduleSlots
                .Include(s => s.FocusSessions)
                .OwnedBy(userId)
                .FirstOrDefaultAsync(s => s.Id == slotId);
        }

        public async Task<string?> GetTaskStatusAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            return await _context.Tasks
                .Where(t => t.Id == taskId)
                .Select(t => t.Status)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
