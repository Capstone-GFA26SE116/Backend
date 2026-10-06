namespace METANOIA.Application.Dto
{
    public class SubTaskDto
    {
        public Guid Id { get; set; }

        public Guid TaskId { get; set; }

        public string Title { get; set; } = null!;

        public int EstimatedMinutes { get; set; }

        public int OrderIndex { get; set; }

        public string Status { get; set; } = null!;
    }
}
