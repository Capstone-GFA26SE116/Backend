using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class Notification
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? ScheduleSlotId { get; set; }

    public string Type { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public DateTime ScheduledAt { get; set; }

    public DateTime? SentAt { get; set; }

    public bool IsRead { get; set; }

    public DateTime? ReadAt { get; set; }

    public virtual ScheduleSlot? ScheduleSlot { get; set; }

    public virtual User User { get; set; } = null!;
}
