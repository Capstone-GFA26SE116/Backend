namespace METANOIA.Application.Dto
{
    public class ProjectDocumentDto
    {
        public Guid Id { get; set; }

        public Guid ProjectId { get; set; }

        public string Name { get; set; } = null!;

        public string SourceType { get; set; } = null!;

        public string? LinkUrl { get; set; }

        public long? SizeBytes { get; set; }

        public string Tag { get; set; } = null!;

        public DateTimeOffset UploadedAt { get; set; }
    }
}
