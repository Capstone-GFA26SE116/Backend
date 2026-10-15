namespace METANOIA.Application.Dto
{
    public class CalendarDto
    {
        public DateTimeOffset From { get; set; }

        public DateTimeOffset To { get; set; }

        // Active | Expired | Revoked | NotConnected. Khác Active nghĩa là event Google có thể cũ hoặc không có
        public string GoogleCalendarStatus { get; set; } = null!;

        // Khung giờ làm việc của user, để frontend tô vùng ngoài giờ làm
        public string TimeZone { get; set; } = null!;

        public TimeOnly WorkDayStart { get; set; }

        public TimeOnly WorkDayEnd { get; set; }

        public bool WorksOnWeekend { get; set; }

        // Sắp theo giờ bắt đầu
        public List<CalendarItemDto> Items { get; set; } = new();
    }

    public class CalendarItemDto
    {
        // Slot: khung giờ làm việc của METANOIA. Google: event trên Google Calendar (giờ bận)
        public string Source { get; set; } = null!;

        public string Id { get; set; } = null!;

        // Slot: tên task. Google: tiêu đề event
        public string Title { get; set; } = null!;

        public DateTimeOffset Start { get; set; }

        public DateTimeOffset End { get; set; }

        public bool IsAllDay { get; set; }

        // ---- Chỉ có với Slot (Google thì để null) ----

        // Proposed | Approved | Done | Skipped | Missed | Cancelled
        public string? Status { get; set; }

        public Guid? TaskId { get; set; }

        public Guid? SubTaskId { get; set; }

        // null nếu slot dành cho cả task, không riêng subtask nào
        public string? SubTaskTitle { get; set; }

        public Guid? ProjectId { get; set; }

        public string? ProjectName { get; set; }

        // null nếu dự án không có khách hàng
        public string? ClientName { get; set; }

        public string? DomainName { get; set; }

        // Flexible | Fixed
        public string? TaskType { get; set; }

        public string? TaskStatus { get; set; }

        public DateTimeOffset? TaskDeadline { get; set; }

        public int? ProposedMinutes { get; set; }

        public string? Explanation { get; set; }

        public bool? IsManualOverride { get; set; }

        public bool? SyncFailed { get; set; }
    }
}
