using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class PaymentTransactionRepository : IPaymentTransactionRepository
    {
        private readonly AppDbContext _context;

        public PaymentTransactionRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<PaymentTransaction>> GetAsync(Guid? userId, CancellationToken cancellationToken = default)
        {
            var query = _context.PaymentTransactions.AsQueryable();
            if (userId is { } id)
            {
                query = query.Where(p => p.UserId == id);
            }

            return await query.OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);
        }

        public Task<PaymentTransaction?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken = default)
        {
            return _context.PaymentTransactions.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        }

        public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Users.AnyAsync(u => u.Id == userId, cancellationToken);
        }

        public Task<bool> PlanExistsAsync(Guid planId, CancellationToken cancellationToken = default)
        {
            return _context.SubscriptionPlans.AnyAsync(p => p.Id == planId, cancellationToken);
        }

        public Task<bool> OrderCodeExistsAsync(long orderCode, Guid excludeId, CancellationToken cancellationToken = default)
        {
            return _context.PaymentTransactions.AnyAsync(p => p.OrderCode == orderCode && p.Id != excludeId, cancellationToken);
        }

        public Task<bool> HasInvoiceAsync(Guid paymentId, CancellationToken cancellationToken = default)
        {
            return _context.Invoices.AnyAsync(i => i.PaymentTransactionId == paymentId, cancellationToken);
        }

        public Task<bool> HasSubscriptionAsync(Guid paymentId, CancellationToken cancellationToken = default)
        {
            return _context.UserSubscriptions.AnyAsync(s => s.PaymentTransactionId == paymentId, cancellationToken);
        }

        public async Task AddAsync(PaymentTransaction payment, CancellationToken cancellationToken = default)
        {
            await _context.PaymentTransactions.AddAsync(payment, cancellationToken);
        }

        public void Remove(PaymentTransaction payment)
        {
            _context.PaymentTransactions.Remove(payment);
        }
    }
}
