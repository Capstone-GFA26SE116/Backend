namespace METANOIA.Application.Interfaces.Services
{
    public interface IPayOSClient
    {
        Task<PayOSPaymentLink> CreatePaymentLinkAsync(PayOSCreateRequest request, CancellationToken cancellationToken = default);

        // null nếu PayOS không biết orderCode này (link chưa từng được tạo)
        Task<PayOSPaymentInfo?> GetPaymentInfoAsync(long orderCode, CancellationToken cancellationToken = default);

        Task<PayOSCancelResult> CancelPaymentLinkAsync(long orderCode, string reason, CancellationToken cancellationToken = default);

        Task ConfirmWebhookAsync(string webhookUrl, CancellationToken cancellationToken = default);

        // null nếu body không đúng định dạng hoặc chữ ký sai
        PayOSWebhookData? VerifyWebhook(string rawBody);
    }

    public record PayOSCreateRequest(long OrderCode, long Amount, string Description, string ItemName, DateTime ExpiresAtUtc);

    public record PayOSPaymentLink(string PaymentLinkId, string CheckoutUrl, string? QrCode);

    // Status: PENDING | PROCESSING | PAID | EXPIRED | UNDERPAID | CANCELLED
    public record PayOSPaymentInfo(
        string Status,
        long Amount,
        long AmountPaid,
        string? PaymentLinkId,
        string? CheckoutUrl,
        string? QrCode,
        DateTime? PaidAtUtc);

    public enum PayOSCancelResult
    {
        Cancelled,
        // Đơn đã thanh toán, đã hủy hoặc đã hết hạn: cần đối soát lại trạng thái thật
        NotCancellable,
        NotFound
    }

    public record PayOSWebhookData(bool Success, long OrderCode, long Amount, string? PaymentLinkId, DateTime? PaidAtUtc);
}
