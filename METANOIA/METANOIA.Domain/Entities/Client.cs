using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class Client
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = null!;

    public string? ContactEmail { get; set; }

    public string? Phone { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Project> Projects { get; set; } = new List<Project>();

    public virtual User User { get; set; } = null!;
}
