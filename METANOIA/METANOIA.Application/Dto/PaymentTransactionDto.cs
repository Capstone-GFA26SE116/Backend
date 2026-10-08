namespace METANOIA.Application.Dto
{
    public class PaymentTransactionDto
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public Guid PlanId { get; set; }

        public long OrderCode { get; set; }

        public string Gateway { get; set; } = null!;

        public long Amount { get; set; }

        public string Status { get; set; } = null!;

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }

        public DateTimeOffset? PaidAt { get; set; }

        public DateTimeOffset? WebhookReceivedAt { get; set; }
    }
}
