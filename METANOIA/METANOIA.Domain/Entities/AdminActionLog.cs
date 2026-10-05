using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class AdminActionLog
{
    public Guid Id { get; set; }

    public Guid AdminId { get; set; }

    public string Action { get; set; } = null!;

    public string TargetType { get; set; } = null!;

    public Guid TargetId { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public string Reason { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual User Admin { get; set; } = null!;
}
