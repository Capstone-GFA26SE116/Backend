using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class BlindTestSession
{
    public Guid Id { get; set; }

    public string Status { get; set; } = null!;

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public virtual ICollection<ScheduleProposal> ScheduleProposals { get; set; } = new List<ScheduleProposal>();
}
