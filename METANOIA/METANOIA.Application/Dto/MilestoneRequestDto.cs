namespace METANOIA.Application.Dto
{
    public class MilestoneRequestDto
    {
        public string Name { get; set; } = null!;

        public int OrderIndex { get; set; }

        public DateOnly? DueDate { get; set; }

        public string? Status { get; set; }

        public int RevisionCount { get; set; }
    }
}
