namespace METANOIA.Application.Dto
{
    public class CalendarEventRequestDto
    {
        public string Title { get; set; } = null!;

        public string? Description { get; set; }

        public string? Location { get; set; }

        public DateTimeOffset Start { get; set; }

        public DateTimeOffset End { get; set; }
    }
}
