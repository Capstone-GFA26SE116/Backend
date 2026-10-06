namespace METANOIA.Application.Dto
{
    public class UserSubscriptionRequestDto
    {
        public Guid UserId { get; set; }

        public Guid PlanId { get; set; }

        public Guid? PaymentTransactionId { get; set; }

        public DateTimeOffset StartAt { get; set; }

        public DateTimeOffset? EndAt { get; set; }

        public string? Status { get; set; }

        public int AiCallsUsed { get; set; }

        public DateTimeOffset? QuotaResetAt { get; set; }
    }
}
