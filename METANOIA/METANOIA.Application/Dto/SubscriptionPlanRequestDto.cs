namespace METANOIA.Application.Dto
{
    public class SubscriptionPlanRequestDto
    {
        public string Name { get; set; } = null!;

        public long Price { get; set; }

        public int DurationDays { get; set; } = 30;

        public int? AiCallLimit { get; set; }

        public int StorageLimitMb { get; set; }

        public int? ProjectLimit { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
