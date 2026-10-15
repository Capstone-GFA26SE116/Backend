using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IPaymentTransactionRepository
    {
        Task<IReadOnlyList<PaymentTransaction>> GetAsync(Guid? userId, CancellationToken cancellationToken = default);

        Task<PaymentTransaction?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken = default);

        Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<bool> PlanExistsAsync(Guid planId, CancellationToken cancellationToken = default);

        Task<bool> OrderCodeExistsAsync(long orderCode, Guid excludeId, CancellationToken cancellationToken = default);

        Task<PaymentTransaction?> GetByOrderCodeAsync(long orderCode, CancellationToken cancellationToken = default);

        Task<SubscriptionPlan?> GetPlanAsync(Guid planId, CancellationToken cancellationToken = default);

        // Giao dịch Pending của user cho gói này mà link thanh toán chưa hết hạn
        Task<PaymentTransaction?> FindOpenPendingAsync(Guid userId, Guid planId, DateTime now, CancellationToken cancellationToken = default);

        // Pending tạo trước thời điểm `createdBefore`: webhook có thể đã bị lỡ, cần hỏi PayOS
        Task<IReadOnlyList<Guid>> GetStalePendingIdsAsync(DateTime createdBefore, int take, CancellationToken cancellationToken = default);

        // Đã Succeeded nhưng thiếu gói đăng ký (và chưa có yêu cầu hoàn tiền) hoặc thiếu hóa đơn: lỡ ghi giữa chừng, hoặc giao dịch cũ trước khi có hóa đơn tự động
        // refundsEnabled = false: không truy vấn bảng PaymentRefund (bảng có thể chưa được tạo)
        Task<IReadOnlyList<Guid>> GetSucceededNeedingFulfillmentIdsAsync(int take, bool refundsEnabled, CancellationToken cancellationToken = default);

        Task<bool> HasRefundAsync(Guid paymentId, CancellationToken cancellationToken = default);

        Task<bool> HasInvoiceAsync(Guid paymentId, CancellationToken cancellationToken = default);

        Task<bool> HasSubscriptionAsync(Guid paymentId, CancellationToken cancellationToken = default);

        Task AddAsync(PaymentTransaction payment, CancellationToken cancellationToken = default);

        void Remove(PaymentTransaction payment);
    }
}
