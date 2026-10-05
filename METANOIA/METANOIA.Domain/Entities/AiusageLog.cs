using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class AiusageLog
{
    public Guid Id { get; set; }

    public Guid UserSubscriptionId { get; set; }

    public string Feature { get; set; } = null!;

    public string Model { get; set; } = null!;

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public decimal Cost { get; set; }

    public int LatencyMs { get; set; }

    public bool Succeeded { get; set; }

    public bool QuotaCharged { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual UserSubscription UserSubscription { get; set; } = null!;
}
