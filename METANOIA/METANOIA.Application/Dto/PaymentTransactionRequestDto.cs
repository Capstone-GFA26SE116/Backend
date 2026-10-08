namespace METANOIA.Application.Dto
{
    public class PaymentTransactionRequestDto
    {
        public Guid UserId { get; set; }

        public Guid PlanId { get; set; }

        public long OrderCode { get; set; }

        public string Gateway { get; set; } = null!;

        public long Amount { get; set; }

        public string? Status { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }

        public DateTimeOffset? PaidAt { get; set; }

        public DateTimeOffset? WebhookReceivedAt { get; set; }
    }
}
