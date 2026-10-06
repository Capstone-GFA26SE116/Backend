namespace METANOIA.Application.Dto
{
    public class TaskRequestDto
    {
        public string Title { get; set; } = null!;

        public string? Description { get; set; }

        public string TaskType { get; set; } = null!;

        public string? Status { get; set; }

        public Guid DomainId { get; set; }

        public Guid? MilestoneId { get; set; }

        public DateTimeOffset? Deadline { get; set; }

        public int? EstimatedMinutes { get; set; }

        public int? AiEstimatedMinutes { get; set; }

        public DateTimeOffset? FixedStart { get; set; }

        public DateTimeOffset? FixedEnd { get; set; }
    }
}
