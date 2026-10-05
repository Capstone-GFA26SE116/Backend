using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class GoogleCalendarEvent
{
    public Guid Id { get; set; }

    public Guid GoogleConnectionId { get; set; }

    public string GoogleEventId { get; set; } = null!;

    public string Title { get; set; } = null!;

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public bool IsAllDay { get; set; }

    public bool IsCreatedByMetanoia { get; set; }

    public string Etag { get; set; } = null!;

    public bool IsCancelled { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual GoogleConnection GoogleConnection { get; set; } = null!;
}
