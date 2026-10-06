namespace METANOIA.Application.Dto
{
    public class SubTaskRequestDto
    {
        public string Title { get; set; } = null!;

        public int EstimatedMinutes { get; set; }

        public int OrderIndex { get; set; }

        public string? Status { get; set; }
    }
}
