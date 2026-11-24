using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class PaymentTransaction
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid PlanId { get; set; }

    public long OrderCode { get; set; }

    public string Gateway { get; set; } = null!;

    public long Amount { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? PaidAt { get; set; }

    public DateTime? WebhookReceivedAt { get; set; }

    public virtual Invoice? Invoice { get; set; }

    public virtual SubscriptionPlan Plan { get; set; } = null!;

    public virtual User User { get; set; } = null!;

    public virtual UserSubscription? UserSubscription { get; set; }
}
