using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class UserDomainProfile
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid DomainId { get; set; }

    public decimal Kbias { get; set; }

    public int SampleCount { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Domain Domain { get; set; } = null!;

    public virtual ICollection<KbiasUpdateLog> KbiasUpdateLogs { get; set; } = new List<KbiasUpdateLog>();

    public virtual User User { get; set; } = null!;
}
