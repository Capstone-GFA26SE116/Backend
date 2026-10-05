using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class SubscriptionPlan
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public long Price { get; set; }

    public int DurationDays { get; set; }

    public int? AiCallLimit { get; set; }

    public int StorageLimitMb { get; set; }

    public int? ProjectLimit { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<PaymentTransaction> PaymentTransactions { get; set; } = new List<PaymentTransaction>();

    public virtual ICollection<UserSubscription> UserSubscriptions { get; set; } = new List<UserSubscription>();
}
