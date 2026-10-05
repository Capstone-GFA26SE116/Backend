using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class SubTask
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }

    public string Title { get; set; } = null!;

    public int EstimatedMinutes { get; set; }

    public int OrderIndex { get; set; }

    public string Status { get; set; } = null!;

    public virtual ICollection<FocusSession> FocusSessions { get; set; } = new List<FocusSession>();

    public virtual ICollection<ScheduleSlot> ScheduleSlots { get; set; } = new List<ScheduleSlot>();

    public virtual Task Task { get; set; } = null!;

    public virtual ICollection<TaskExecutionMemory> TaskExecutionMemories { get; set; } = new List<TaskExecutionMemory>();
}
