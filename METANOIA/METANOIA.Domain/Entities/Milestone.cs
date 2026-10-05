using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class Milestone
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

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();
}
