using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace METANOIA.Infrastructure.Repositories
{
    public class RevenueRepository : IRevenueRepository
    {
        private const string Succeeded = "Succeeded";
        private const string Completed = "Completed";

        private readonly AppDbContext _context;

        public RevenueRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<RevenuePaidRow>> GetPaidAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            var rows = await _context.PaymentTransactions
                .Where(p => p.Status == Succeeded && p.PaidAt != null && p.PaidAt >= from && p.PaidAt < to)
                .Select(p => new { p.PaidAt, p.Amount, p.PlanId, PlanName = p.Plan.Name })
                .ToListAsync(cancellationToken);

            return rows.Select(r => new RevenuePaidRow(r.PaidAt!.Value, r.Amount, r.PlanId, r.PlanName)).ToList();
        }

        public async Task<IReadOnlyList<RevenueRefundRow>> GetCompletedRefundsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            var rows = await _context.PaymentRefunds
                .Where(r => r.Status == Completed && (r.ProcessedAt ?? r.CreatedAt) >= from && (r.ProcessedAt ?? r.CreatedAt) < to)
                .Select(r => new
                {
                    At = r.ProcessedAt ?? r.CreatedAt,
                    r.Amount,
                    r.PaymentTransaction.PlanId,
                    PlanName = r.PaymentTransaction.Plan.Name
                })
                .ToListAsync(cancellationToken);

            return rows.Select(r => new RevenueRefundRow(r.At, r.Amount, r.PlanId, r.PlanName)).ToList();
        }

        public async Task<Dictionary<string, int>> CountTransactionsByStatusAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            var rows = await _context.PaymentTransactions
                .Where(p => p.CreatedAt >= from && p.CreatedAt < to)
                .GroupBy(p => p.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            return rows.ToDictionary(r => r.Status, r => r.Count);
        }

        public Task<int> CountInvoicesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            return _context.Invoices.CountAsync(i => i.IssuedAt >= from && i.IssuedAt < to, cancellationToken);
        }

        public async Task<int> CountNewPayingUsersAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            var firstPayments = await _context.PaymentTransactions
                .Where(p => p.Status == Succeeded && p.PaidAt != null)
                .GroupBy(p => p.UserId)
                .Select(g => g.Min(p => p.PaidAt))
                .ToListAsync(cancellationToken);

            return firstPayments.Count(first => first >= from && first < to);
        }
    }
}
