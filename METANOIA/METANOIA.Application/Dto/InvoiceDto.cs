namespace METANOIA.Application.Dto
{
    public class InvoiceDto
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

        public DateTimeOffset IssuedAt { get; set; }
    }
}
