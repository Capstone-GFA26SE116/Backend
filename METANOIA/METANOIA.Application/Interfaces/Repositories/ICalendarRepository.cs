using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    // Một slot cùng các thông tin của task, subtask, dự án, khách hàng, loại việc, lấy bằng MỘT câu truy vấn có join
    public class CalendarSlotRow
    {
        public Guid Id { get; set; }

        public Guid TaskId { get; set; }

        public Guid? SubTaskId { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime EndTime { get; set; }

        public string Status { get; set; } = null!;

        public int ProposedMinutes { get; set; }

        public string? Explanation { get; set; }

        public bool IsManualOverride { get; set; }

        public bool SyncFailed { get; set; }

        public string TaskTitle { get; set; } = null!;

        public string TaskType { get; set; } = null!;

        public string TaskStatus { get; set; } = null!;

        public DateTime? TaskDeadline { get; set; }

        public string? SubTaskTitle { get; set; }

        public Guid ProjectId { get; set; }

        public string ProjectName { get; set; } = null!;

        public string? ClientName { get; set; }

        public string DomainName { get; set; } = null!;
    }

    public class CalendarWorkingHours
    {
        public string TimeZone { get; set; } = null!;

        public TimeOnly WorkDayStart { get; set; }

        public TimeOnly WorkDayEnd { get; set; }

        public bool WorksOnWeekend { get; set; }
    }

    public interface ICalendarRepository
    {
        // Slot của task thuộc project của user, giao với [from, to), sắp theo giờ bắt đầu
        Task<IReadOnlyList<CalendarSlotRow>> GetSlotsAsync(Guid userId, DateTime from, DateTime to, bool includeCancelled, CancellationToken cancellationToken = default);

        // Event Google đã cache của user giao với [from, to), chỉ lấy event chưa bị hủy và KHÔNG do METANOIA tạo
        Task<IReadOnlyList<GoogleCalendarEvent>> GetGoogleEventsAsync(Guid userId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

        Task<CalendarWorkingHours?> GetWorkingHoursAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}
