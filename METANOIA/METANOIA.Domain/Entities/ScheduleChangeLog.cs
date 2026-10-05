using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class ScheduleChangeLog
{
    public Guid Id { get; set; }

    public Guid ScheduleSlotId { get; set; }

    public string ChangeType { get; set; } = null!;

    public string Source { get; set; } = null!;

    public DateTime? OldStart { get; set; }

    public DateTime? OldEnd { get; set; }

    public DateTime? NewStart { get; set; }

    public DateTime? NewEnd { get; set; }

    public DateTime ChangedAt { get; set; }

    public virtual ScheduleSlot ScheduleSlot { get; set; } = null!;
}
