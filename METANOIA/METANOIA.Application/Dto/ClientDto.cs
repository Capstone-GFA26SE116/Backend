namespace METANOIA.Application.Dto
{
    public class ClientDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public string? ContactEmail { get; set; }

        public string? Phone { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
