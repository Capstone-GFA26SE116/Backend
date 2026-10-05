using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class CircadianUpdateLog
{
    public Guid Id { get; set; }

    public Guid UserCircadianProfileId { get; set; }

    public decimal OldScore { get; set; }

    public decimal NewScore { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual UserCircadianProfile UserCircadianProfile { get; set; } = null!;
}
