using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface ICalendarService
    {
        // Gộp slot của METANOIA và event Google (đã cache) trong khoảng [from, to)
        Task<CalendarDto> GetCalendarAsync(Guid userId, DateTimeOffset from, DateTimeOffset to, bool includeCancelled, CancellationToken cancellationToken = default);
    }
}
