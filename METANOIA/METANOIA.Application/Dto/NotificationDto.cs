namespace METANOIA.Application.Dto
{
    public class NotificationDto
    {
        public Guid Id { get; set; }

        // Loại thông báo, ví dụ START_PROMPT, NEAR_END, OVERRUN, AUTO_TIMEOUT
        public string Type { get; set; } = null!;

        public string Title { get; set; } = null!;

        public string Message { get; set; } = null!;

        public Guid? ScheduleSlotId { get; set; }

        public DateTimeOffset? SentAt { get; set; }

        public bool IsRead { get; set; }

        public DateTimeOffset? ReadAt { get; set; }
    }
}
