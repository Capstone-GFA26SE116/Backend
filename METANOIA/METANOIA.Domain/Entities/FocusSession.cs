using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class FocusSession
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }

    public Guid? SubTaskId { get; set; }

    public Guid? ScheduleSlotId { get; set; }

    public Guid? FocusTrackId { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public string? StopReason { get; set; }

    public string? Note { get; set; }

    public string CaptureMethod { get; set; } = null!;

    public bool IsConfirmed { get; set; }

    public virtual FocusTrack? FocusTrack { get; set; }

    public virtual ScheduleSlot? ScheduleSlot { get; set; }

    public virtual SubTask? SubTask { get; set; }

    public virtual Task Task { get; set; } = null!;
}
