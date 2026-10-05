namespace METANOIA.Application.Dto
{
    public class ProjectDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public DateOnly? StartDate { get; set; }

        public DateOnly? Deadline { get; set; }

        public string Status { get; set; } = null!;

        public Guid? ClientId { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
