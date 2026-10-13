namespace METANOIA.Infrastructure.Services
{
    // Cấu hình mục "PayOS" trong appsettings.json
    public class PayOSOptions
    {
        public string ApiBaseUrl { get; set; } = "https://api-merchant.payos.vn";

        public string ClientId { get; set; } = "";

        public string ApiKey { get; set; } = "";

        public string ChecksumKey { get; set; } = "";

        public string ReturnUrl { get; set; } = "";

        public string CancelUrl { get; set; } = "";

        public string WebhookUrl { get; set; } = "";

        // PayOS giới hạn độ dài mô tả (tối đa 9 ký tự với tài khoản ngân hàng chưa liên kết qua payOS)
        public string Description { get; set; } = "METANOIA";

        public bool IsConfigured =>
            IsReal(ClientId) && IsReal(ApiKey) && IsReal(ChecksumKey) && IsReal(ReturnUrl) && IsReal(CancelUrl);

        private static bool IsReal(string value) =>
            !string.IsNullOrWhiteSpace(value) && !value.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
    }

    // Cấu hình mục "Payments" trong appsettings.json
    public class PaymentOptions
    {
        // Link thanh toán sống bao lâu (phút) kể từ lúc tạo
        public int LinkExpiryMinutes { get; set; } = 15;

        // Chờ thêm bao nhiêu giây sau ExpiresAt rồi mới hủy link / đánh dấu Expired
        public int ExpiryGraceSeconds { get; set; } = 60;

        // Cho phép khách tự yêu cầu hoàn tiền trong bao nhiêu ngày kể từ lúc thanh toán
        public int RefundWindowDays { get; set; } = 7;

        public bool ReconciliationEnabled { get; set; } = true;

        public int ReconciliationIntervalSeconds { get; set; } = 60;

        // Chỉ đối soát Pending cũ hơn mức này để không giành việc với webhook vừa tới
        public int PendingStaleAfterSeconds { get; set; } = 120;
    }
}
