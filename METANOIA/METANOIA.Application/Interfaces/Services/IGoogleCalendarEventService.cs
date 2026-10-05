using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IGoogleCalendarEventService
    {
        Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(Guid userId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

        Task<CalendarEventDto> GetEventAsync(Guid userId, string eventId, CancellationToken cancellationToken = default);

        Task<CalendarEventDto> CreateEventAsync(Guid userId, CalendarEventRequestDto request, CancellationToken cancellationToken = default);

        Task<CalendarEventDto> UpdateEventAsync(Guid userId, string eventId, CalendarEventRequestDto request, CancellationToken cancellationToken = default);

        Task DeleteEventAsync(Guid userId, string eventId, CancellationToken cancellationToken = default);
    }
}
