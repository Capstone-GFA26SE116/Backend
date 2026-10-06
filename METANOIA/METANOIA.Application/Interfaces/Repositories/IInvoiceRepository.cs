using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IInvoiceRepository
    {
        Task<IReadOnlyList<Invoice>> GetAsync(Guid? userId, CancellationToken cancellationToken = default);

        Task<Invoice?> GetByIdAsync(Guid invoiceId, CancellationToken cancellationToken = default);

        Task<PaymentTransaction?> GetPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);

        Task<bool> InvoiceNumberExistsAsync(string invoiceNumber, Guid excludeId, CancellationToken cancellationToken = default);

        Task<bool> PaymentHasInvoiceAsync(Guid paymentId, Guid excludeId, CancellationToken cancellationToken = default);

        Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default);

        void Remove(Invoice invoice);
    }
}
