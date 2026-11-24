using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class ScheduleProposal
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public short Level { get; set; }

    public string Status { get; set; } = null!;

    public bool IsFeasible { get; set; }

    public int? MissingMinutes { get; set; }

    public string Explanation { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<ScheduleSlot> ScheduleSlots { get; set; } = new List<ScheduleSlot>();

    public virtual User User { get; set; } = null!;
}
