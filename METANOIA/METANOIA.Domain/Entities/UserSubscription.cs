using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class UserSubscription
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid PlanId { get; set; }

    public Guid? PaymentTransactionId { get; set; }

    public DateTime StartAt { get; set; }

    public DateTime? EndAt { get; set; }

    public string Status { get; set; } = null!;

    public int AiCallsUsed { get; set; }

    public DateTime QuotaResetAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<AiusageLog> AiusageLogs { get; set; } = new List<AiusageLog>();

    public virtual PaymentTransaction? PaymentTransaction { get; set; }

    public virtual SubscriptionPlan Plan { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
