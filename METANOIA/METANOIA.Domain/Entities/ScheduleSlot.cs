using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class ScheduleSlot
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }

    public Guid? SubTaskId { get; set; }

    public Guid? ScheduleProposalId { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public DateTime? OriginalStartTime { get; set; }

    public bool IsManualOverride { get; set; }

    public int ProposedMinutes { get; set; }

    public decimal? KbiasUsed { get; set; }

    public string? Explanation { get; set; }

    public string Status { get; set; } = null!;

    public string? GoogleEventId { get; set; }

    public bool SyncFailed { get; set; }

    public virtual ICollection<FocusSession> FocusSessions { get; set; } = new List<FocusSession>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<ScheduleChangeLog> ScheduleChangeLogs { get; set; } = new List<ScheduleChangeLog>();

    public virtual ScheduleProposal? ScheduleProposal { get; set; }

    public virtual SubTask? SubTask { get; set; }

    public virtual Task Task { get; set; } = null!;
}
