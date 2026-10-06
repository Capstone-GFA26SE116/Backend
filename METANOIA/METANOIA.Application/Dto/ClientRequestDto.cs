namespace METANOIA.Application.Dto
{
    public class ClientRequestDto
    {
        public string Name { get; set; } = null!;

        public string? ContactEmail { get; set; }

        public string? Phone { get; set; }

        public string? Notes { get; set; }
    }
}
