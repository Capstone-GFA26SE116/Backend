using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class GoogleCalendarEventRepository : IGoogleCalendarEventRepository
    {
        private readonly AppDbContext _context;

        public GoogleCalendarEventRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<GoogleCalendarEvent?> GetByGoogleEventIdAsync(Guid connectionId, string googleEventId, CancellationToken cancellationToken = default)
        {
            return _context.GoogleCalendarEvents
                .FirstOrDefaultAsync(e => e.GoogleConnectionId == connectionId && e.GoogleEventId == googleEventId, cancellationToken);
        }

        public async Task AddAsync(GoogleCalendarEvent calendarEvent, CancellationToken cancellationToken = default)
        {
            await _context.GoogleCalendarEvents.AddAsync(calendarEvent, cancellationToken);
        }
    }
}
