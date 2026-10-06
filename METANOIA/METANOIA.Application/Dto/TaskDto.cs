namespace METANOIA.Application.Dto
{
    public class TaskDto
    {
        public Guid Id { get; set; }

        public Guid ProjectId { get; set; }

        public Guid? MilestoneId { get; set; }

        public Guid DomainId { get; set; }

        public string Title { get; set; } = null!;

        public string? Description { get; set; }

        public string TaskType { get; set; } = null!;

        public string Status { get; set; } = null!;

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset? CompletedAt { get; set; }

        public DateTimeOffset? Deadline { get; set; }

        public int? EstimatedMinutes { get; set; }

        public int? AiEstimatedMinutes { get; set; }

        public DateTimeOffset? FixedStart { get; set; }

        public DateTimeOffset? FixedEnd { get; set; }
    }
}
