using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class FocusTrack
{
    public Guid Id { get; set; }

    public string Title { get; set; } = null!;

    public string Artist { get; set; } = null!;

    public string FileUrl { get; set; } = null!;

    public int DurationSeconds { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<FocusSession> FocusSessions { get; set; } = new List<FocusSession>();
}
