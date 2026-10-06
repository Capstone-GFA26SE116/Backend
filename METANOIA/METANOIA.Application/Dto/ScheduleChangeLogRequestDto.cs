namespace METANOIA.Application.Dto
{
    public class ScheduleChangeLogRequestDto
    {
        public string ChangeType { get; set; } = null!;

        public string Source { get; set; } = null!;

        public DateTimeOffset? OldStart { get; set; }

        public DateTimeOffset? OldEnd { get; set; }

        public DateTimeOffset? NewStart { get; set; }

        public DateTimeOffset? NewEnd { get; set; }
    }
}
