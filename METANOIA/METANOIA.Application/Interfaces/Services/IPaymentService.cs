using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public enum WebhookOutcome
    {
        // Thanh toán hợp lệ, gói đã được kích hoạt
        Processed,
        // Webhook gửi lại cho giao dịch đã xử lý: bỏ qua, không kích hoạt lần hai
        Duplicate,
        // Tiền về nhưng không hợp lệ (sai số tiền, đơn đã hủy...): đã mở yêu cầu hoàn tiền (chỉ khi Payments:RefundsEnabled = true)
        RefundOpened,
        // Tiền về nhưng không hợp lệ và chức năng hoàn tiền đang tắt: đã ghi log lỗi, cần xử lý thủ công
        ReviewRequired,
        // Webhook không liên quan (orderCode lạ, success = false)
        Ignored,
        InvalidSignature
    }

    public interface IPaymentService
    {
        Task<CheckoutResultDto> CheckoutAsync(Guid userId, Guid planId, CancellationToken cancellationToken = default);

        // Hỏi PayOS trạng thái thật và cập nhật; dùng khi trang return/cancel được mở hoặc webhook không đến
        Task<PaymentTransactionDto> SyncAsync(Guid userId, bool isAdmin, Guid paymentId, CancellationToken cancellationToken = default);

        Task<PaymentTransactionDto> CancelAsync(Guid userId, bool isAdmin, Guid paymentId, CancellationToken cancellationToken = default);

        Task<WebhookOutcome> HandleWebhookAsync(string rawBody, CancellationToken cancellationToken = default);

        Task ConfirmWebhookAsync(CancellationToken cancellationToken = default);

        // Dành cho worker đối soát nền
        Task<IReadOnlyList<Guid>> GetPaymentIdsNeedingReconciliationAsync(CancellationToken cancellationToken = default);

        Task ReconcilePaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);

        // Hết hạn gói Active quá EndAt và chuyển gói Scheduled đến hạn thành Active
        Task RefreshSubscriptionStatesAsync(CancellationToken cancellationToken = default);

        // Như trên nhưng cho đúng một user: nếu gói Active của họ đã quá EndAt thì xử lý ngay (hết hạn, bật gói xếp hàng hoặc về gói Miễn phí).
        // Gọi khi đọc thông tin của user để không phải chờ lượt chạy kế tiếp của job nền. Không làm gì nếu gói còn hạn.
        Task RefreshUserSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}
