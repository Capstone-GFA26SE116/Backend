namespace METANOIA.Application.Dto
{
    public class CalendarDto
    {
        public DateTimeOffset From { get; set; }

        public DateTimeOffset To { get; set; }

        // Active | Expired | Revoked | NotConnected. Khác Active nghĩa là event Google có thể cũ hoặc không có
        public string GoogleCalendarStatus { get; set; } = null!;

        // Sắp theo giờ bắt đầu
        public List<CalendarItemDto> Items { get; set; } = new();
    }

    public class CalendarItemDto
    {
        // Slot: khung giờ làm việc của METANOIA. Google: event trên Google Calendar (giờ bận)
        public string Source { get; set; } = null!;

        public string Id { get; set; } = null!;

        public string Title { get; set; } = null!;

        public DateTimeOffset Start { get; set; }

        public DateTimeOffset End { get; set; }

        public bool IsAllDay { get; set; }

        // Chỉ có với Slot: Proposed | Approved | Done | Skipped | Missed
        public string? Status { get; set; }

        public Guid? TaskId { get; set; }

        public Guid? SubTaskId { get; set; }

        public Guid? ProjectId { get; set; }
    }
}
