using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class ProjectDocument
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public string Name { get; set; } = null!;

    public string SourceType { get; set; } = null!;

    public string? StoragePath { get; set; }

    public string? LinkUrl { get; set; }

    public long? SizeBytes { get; set; }

    public string Tag { get; set; } = null!;

    public DateTime UploadedAt { get; set; }

    public virtual Project Project { get; set; } = null!;
}
