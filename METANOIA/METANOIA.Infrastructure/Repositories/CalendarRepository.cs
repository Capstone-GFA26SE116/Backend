using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace METANOIA.Infrastructure.Repositories
{
    public class CalendarRepository : ICalendarRepository
    {
        private readonly AppDbContext _context;

        public CalendarRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<ScheduleSlot>> GetSlotsAsync(
            Guid userId, DateTime from, DateTime to, bool includeCancelled, CancellationToken cancellationToken = default)
        {
            var query = _context.ScheduleSlots
                .Include(s => s.Task)
                .Where(s => s.Task.Project.UserId == userId && s.StartTime < to && s.EndTime > from);

            if (!includeCancelled)
            {
                query = query.Where(s => s.Status != "Cancelled");
            }

            return await query.OrderBy(s => s.StartTime).ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<GoogleCalendarEvent>> GetGoogleEventsAsync(
            Guid userId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            return await _context.GoogleCalendarEvents
                .Where(e => e.GoogleConnection.UserId == userId && !e.IsCancelled && e.StartTime < to && e.EndTime > from)
                .OrderBy(e => e.StartTime)
                .ToListAsync(cancellationToken);
        }
    }
}
