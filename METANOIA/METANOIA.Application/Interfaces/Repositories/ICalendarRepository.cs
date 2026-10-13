using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface ICalendarRepository
    {
        // Slot của task thuộc project của user, giao với [from, to). Kèm Task để lấy tiêu đề và ProjectId
        Task<IReadOnlyList<ScheduleSlot>> GetSlotsAsync(Guid userId, DateTime from, DateTime to, bool includeCancelled, CancellationToken cancellationToken = default);

        // Event Google đã cache của user, chưa bị hủy, giao với [from, to)
        Task<IReadOnlyList<GoogleCalendarEvent>> GetGoogleEventsAsync(Guid userId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    }
}
