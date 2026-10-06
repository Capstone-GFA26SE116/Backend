namespace METANOIA.Application.Dto
{
    public class DomainDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public bool IsActive { get; set; }
    }
}
