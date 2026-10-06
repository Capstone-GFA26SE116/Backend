namespace METANOIA.Application.Dto
{
    public class MilestoneDto
    {
        public Guid Id { get; set; }

        public Guid ProjectId { get; set; }

        public string Name { get; set; } = null!;

        public int OrderIndex { get; set; }

        public DateOnly? DueDate { get; set; }

        public string Status { get; set; } = null!;

        public DateTime? DeliveredAt { get; set; }

        public DateTime? ApprovedAt { get; set; }

        public int RevisionCount { get; set; }
    }
}
