using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class UserCircadianProfile
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public short HourOfDay { get; set; }

    public decimal Score { get; set; }

    public string Source { get; set; } = null!;

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<CircadianUpdateLog> CircadianUpdateLogs { get; set; } = new List<CircadianUpdateLog>();

    public virtual User User { get; set; } = null!;
}
