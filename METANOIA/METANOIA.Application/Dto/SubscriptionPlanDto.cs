namespace METANOIA.Application.Dto
{
    public class SubscriptionPlanDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public long Price { get; set; }

        public int DurationDays { get; set; }

        public int? AiCallLimit { get; set; }

        public int StorageLimitMb { get; set; }

        public int? ProjectLimit { get; set; }

        public bool IsActive { get; set; }
    }
}
