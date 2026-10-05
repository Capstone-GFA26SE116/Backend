namespace METANOIA.Application.Dto
{
    public class ProjectRequestDto
    {
        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public DateOnly? StartDate { get; set; }

        public DateOnly? Deadline { get; set; }

        public Guid? ClientId { get; set; }
    }
}
