namespace METANOIA.Application.Dto
{
    public class ProjectProgressDto
    {
        public Guid ProjectId { get; set; }

        public string Name { get; set; } = null!;

        public string Status { get; set; } = null!;

        public DateOnly? Deadline { get; set; }

        public int TotalTasks { get; set; }

        public int CompletedTasks { get; set; }

        public double ProgressPercent { get; set; }

        public int EstimatedMinutes { get; set; }

        public int ActualMinutes { get; set; }

        // Dương: đã dùng nhiều hơn dự kiến
        public int VarianceMinutes { get; set; }

        public List<ProjectTaskProgressDto> Tasks { get; set; } = new();
    }

    public class ProjectTaskProgressDto
    {
        public Guid TaskId { get; set; }

        public string Title { get; set; } = null!;

        public string Status { get; set; } = null!;

        public bool IsCompleted { get; set; }

        public int EstimatedMinutes { get; set; }

        public int ActualMinutes { get; set; }
    }
}
