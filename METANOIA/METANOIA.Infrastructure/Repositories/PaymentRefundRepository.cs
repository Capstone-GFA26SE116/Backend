using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class PaymentRefundRepository : IPaymentRefundRepository
    {
        private readonly AppDbContext _context;

        public PaymentRefundRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<PaymentRefund>> GetAsync(Guid? userId, string? status, CancellationToken cancellationToken = default)
        {
            var query = _context.PaymentRefunds.Include(r => r.PaymentTransaction).AsQueryable();
            if (userId is { } id)
            {
                query = query.Where(r => r.PaymentTransaction.UserId == id);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(r => r.Status == status);
            }

            return await query.OrderByDescending(r => r.CreatedAt).ToListAsync(cancellationToken);
        }

        public Task<PaymentRefund?> GetByIdAsync(Guid refundId, CancellationToken cancellationToken = default)
        {
            return _context.PaymentRefunds.Include(r => r.PaymentTransaction)
                .FirstOrDefaultAsync(r => r.Id == refundId, cancellationToken);
        }

        public Task<PaymentRefund?> GetActiveByPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
        {
            return _context.PaymentRefunds
                .FirstOrDefaultAsync(r => r.PaymentTransactionId == paymentId && r.Status != "Rejected", cancellationToken);
        }

        public async Task AddAsync(PaymentRefund refund, CancellationToken cancellationToken = default)
        {
            await _context.PaymentRefunds.AddAsync(refund, cancellationToken);
        }
    }
}
