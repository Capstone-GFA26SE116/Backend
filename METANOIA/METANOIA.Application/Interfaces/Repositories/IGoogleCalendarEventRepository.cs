using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IGoogleCalendarEventRepository
    {
        Task<GoogleCalendarEvent?> GetByGoogleEventIdAsync(Guid connectionId, string googleEventId, CancellationToken cancellationToken = default);

        Task AddAsync(GoogleCalendarEvent calendarEvent, CancellationToken cancellationToken = default);
    }
}
