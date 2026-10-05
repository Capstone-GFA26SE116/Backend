using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class Task
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? MilestoneId { get; set; }

    public Guid DomainId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string TaskType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? Deadline { get; set; }

    public int? EstimatedMinutes { get; set; }

    public int? AiEstimatedMinutes { get; set; }

    public DateTime? FixedStart { get; set; }

    public DateTime? FixedEnd { get; set; }

    public virtual ICollection<ChatConversation> ChatConversations { get; set; } = new List<ChatConversation>();

    public virtual Domain Domain { get; set; } = null!;

    public virtual ICollection<FocusSession> FocusSessions { get; set; } = new List<FocusSession>();

    public virtual KbiasUpdateLog? KbiasUpdateLog { get; set; }

    public virtual Milestone? Milestone { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<ScheduleSlot> ScheduleSlots { get; set; } = new List<ScheduleSlot>();

    public virtual ICollection<SubTask> SubTasks { get; set; } = new List<SubTask>();

    public virtual ICollection<TaskExecutionMemory> TaskExecutionMemories { get; set; } = new List<TaskExecutionMemory>();
}
