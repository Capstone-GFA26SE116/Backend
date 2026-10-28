using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace METANOIA.Infrastructure.Repositories
{
    public class FocusSessionRepository : IFocusSessionRepository
    {
        private readonly AppDbContext _context;

        public FocusSessionRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<FocusSession?> GetRunningAsync(Guid userId)
        {
            return await _context.FocusSessions
                .FirstOrDefaultAsync(s => s.Task.Project.UserId == userId && s.EndedAt == null);
        }

        public async Task<FocusSession?> GetRunningWithDetailsAsync(Guid userId)
        {
            return await _context.FocusSessions
                .Include(s => s.Task)
                .Include(s => s.ScheduleSlot)
                .FirstOrDefaultAsync(s => s.Task.Project.UserId == userId && s.EndedAt == null);
        }

        public async Task<FocusSession?> GetByIdAsync(Guid sessionId)
        {
            return await _context.FocusSessions
                .Include(s => s.Task)
                    .ThenInclude(t => t.Project)
                .FirstOrDefaultAsync(s => s.Id == sessionId);
        }

        public async Task<List<FocusSession>> GetEndedSessionsByTaskIdAsync(Guid taskId, Guid excludeSessionId)
        {
            return await _context.FocusSessions
                .Where(s => s.TaskId == taskId && s.EndedAt != null && s.Id != excludeSessionId)
                .ToListAsync();
        }

        public async Task<List<FocusSession>> GetUnconfirmedAutoTimeoutAsync(Guid userId)
        {
            return await _context.FocusSessions
                .Include(s => s.Task)
                .Where(s => s.Task.Project.UserId == userId
                            && s.EndedAt != null
                            && !s.IsConfirmed
                            && s.CaptureMethod == "AutoTimeout")
                .OrderByDescending(s => s.EndedAt)
                .ToListAsync();
        }

        public async System.Threading.Tasks.Task AddAsync(FocusSession session)
        {
            await _context.FocusSessions.AddAsync(session);
        }
    }
}
