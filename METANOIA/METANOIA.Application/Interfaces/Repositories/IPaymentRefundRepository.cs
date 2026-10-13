using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IPaymentRefundRepository
    {
        Task<IReadOnlyList<PaymentRefund>> GetAsync(Guid? userId, string? status, CancellationToken cancellationToken = default);

        Task<PaymentRefund?> GetByIdAsync(Guid refundId, CancellationToken cancellationToken = default);

        // Yêu cầu hoàn tiền chưa bị từ chối của giao dịch (tối đa một)
        Task<PaymentRefund?> GetActiveByPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);

        Task AddAsync(PaymentRefund refund, CancellationToken cancellationToken = default);
    }
}
