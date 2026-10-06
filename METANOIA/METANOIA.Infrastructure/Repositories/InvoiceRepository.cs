using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class InvoiceRepository : IInvoiceRepository
    {
        private readonly AppDbContext _context;

        public InvoiceRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<Invoice>> GetAsync(Guid? userId, CancellationToken cancellationToken = default)
        {
            var query = _context.Invoices.AsQueryable();
            if (userId is { } id)
            {
                query = query.Where(i => i.PaymentTransaction.UserId == id);
            }

            return await query.OrderByDescending(i => i.IssuedAt).ToListAsync(cancellationToken);
        }

        public Task<Invoice?> GetByIdAsync(Guid invoiceId, CancellationToken cancellationToken = default)
        {
            return _context.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        }

        public Task<PaymentTransaction?> GetPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
        {
            return _context.PaymentTransactions.FirstOrDefaultAsync(t => t.Id == paymentId, cancellationToken);
        }

        public Task<bool> InvoiceNumberExistsAsync(string invoiceNumber, Guid excludeId, CancellationToken cancellationToken = default)
        {
            return _context.Invoices.AnyAsync(i => i.Id != excludeId && i.InvoiceNumber == invoiceNumber, cancellationToken);
        }

        public Task<bool> PaymentHasInvoiceAsync(Guid paymentId, Guid excludeId, CancellationToken cancellationToken = default)
        {
            return _context.Invoices.AnyAsync(i => i.PaymentTransactionId == paymentId && i.Id != excludeId, cancellationToken);
        }

        public async Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default)
        {
            await _context.Invoices.AddAsync(invoice, cancellationToken);
        }

        public void Remove(Invoice invoice)
        {
            _context.Invoices.Remove(invoice);
        }
    }
}
