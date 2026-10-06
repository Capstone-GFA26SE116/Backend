namespace METANOIA.Application.Dto
{
    public class UserSubscriptionDto
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public Guid PlanId { get; set; }

        public Guid? PaymentTransactionId { get; set; }

        public DateTimeOffset StartAt { get; set; }

        public DateTimeOffset? EndAt { get; set; }

        public string Status { get; set; } = null!;

        public int AiCallsUsed { get; set; }

        public DateTimeOffset QuotaResetAt { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}
