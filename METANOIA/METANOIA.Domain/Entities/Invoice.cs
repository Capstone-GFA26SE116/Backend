using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class Invoice
{
    public Guid Id { get; set; }

    public Guid PaymentTransactionId { get; set; }

    public string InvoiceNumber { get; set; } = null!;

    public string PlanName { get; set; } = null!;

    public long Amount { get; set; }

    public string BillingName { get; set; } = null!;

    public string BillingEmail { get; set; } = null!;

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public DateTime IssuedAt { get; set; }

    public virtual PaymentTransaction PaymentTransaction { get; set; } = null!;
}
