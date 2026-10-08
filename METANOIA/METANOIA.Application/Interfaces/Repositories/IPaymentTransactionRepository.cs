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

        Task<bool> HasInvoiceAsync(Guid paymentId, CancellationToken cancellationToken = default);

        Task<bool> HasSubscriptionAsync(Guid paymentId, CancellationToken cancellationToken = default);

        Task AddAsync(PaymentTransaction payment, CancellationToken cancellationToken = default);

        void Remove(PaymentTransaction payment);
    }
}
