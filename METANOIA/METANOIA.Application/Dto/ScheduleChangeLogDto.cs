namespace METANOIA.Application.Dto
{
    public class ScheduleChangeLogDto
    {
        public Guid Id { get; set; }

        public Guid ScheduleSlotId { get; set; }

        public string ChangeType { get; set; } = null!;

        public string Source { get; set; } = null!;

        public DateTimeOffset? OldStart { get; set; }

        public DateTimeOffset? OldEnd { get; set; }

        public DateTimeOffset? NewStart { get; set; }

        public DateTimeOffset? NewEnd { get; set; }

        public DateTimeOffset ChangedAt { get; set; }
    }
}
