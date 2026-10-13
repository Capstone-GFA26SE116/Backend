namespace METANOIA.Infrastructure.Services
{
    // Cấu hình mục "Subscription" trong appsettings.json
    public class SubscriptionOptions
    {
        // Id của dòng gói Miễn phí trong bảng SubscriptionPlan (Price = 0, không hết hạn)
        public Guid FreePlanId { get; set; }
    }
}
