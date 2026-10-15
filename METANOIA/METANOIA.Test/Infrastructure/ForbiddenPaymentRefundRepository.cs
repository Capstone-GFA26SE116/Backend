using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test.Infrastructure
{
    // Mọi lời gọi đều ném lỗi: nếu luồng thanh toán lỡ dùng bảng PaymentRefund khi hoàn tiền đang tắt thì test sẽ hỏng
    public class ForbiddenPaymentRefundRepository : IPaymentRefundRepository
    {
        private static InvalidOperationException Forbidden() =>
            new("Bảng PaymentRefund không được truy cập khi Payments:RefundsEnabled = false.");

        public Task<IReadOnlyList<PaymentRefund>> GetAsync(Guid? userId, string? status, CancellationToken cancellationToken = default) => throw Forbidden();

        public Task<PaymentRefund?> GetByIdAsync(Guid refundId, CancellationToken cancellationToken = default) => throw Forbidden();

        public Task<PaymentRefund?> GetActiveByPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default) => throw Forbidden();

        public Task AddAsync(PaymentRefund refund, CancellationToken cancellationToken = default) => throw Forbidden();
    }
}
