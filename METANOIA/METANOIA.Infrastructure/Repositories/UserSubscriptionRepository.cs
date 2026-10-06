using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class UserSubscriptionRepository : IUserSubscriptionRepository
    {
        private const string ActiveStatus = "Active";

        private readonly AppDbContext _context;

        public UserSubscriptionRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<UserSubscription>> GetAsync(Guid? userId, CancellationToken cancellationToken = default)
        {
            var query = _context.UserSubscriptions.AsQueryable();
            if (userId is { } id)
            {
                query = query.Where(s => s.UserId == id);
            }

            return await query.OrderByDescending(s => s.StartAt).ToListAsync(cancellationToken);
        }

        public Task<UserSubscription?> GetByIdAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
        {
            return _context.UserSubscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken);
        }

        public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Users.AnyAsync(u => u.Id == userId, cancellationToken);
        }

        public Task<bool> PlanExistsAsync(Guid planId, CancellationToken cancellationToken = default)
        {
            return _context.SubscriptionPlans.AnyAsync(p => p.Id == planId, cancellationToken);
        }

        public Task<bool> PaymentExistsAsync(Guid paymentId, CancellationToken cancellationToken = default)
        {
            return _context.PaymentTransactions.AnyAsync(t => t.Id == paymentId, cancellationToken);
        }

        public Task<bool> PaymentLinkedToOtherSubscriptionAsync(Guid paymentId, Guid excludeId, CancellationToken cancellationToken = default)
        {
            return _context.UserSubscriptions.AnyAsync(
                s => s.PaymentTransactionId == paymentId && s.Id != excludeId, cancellationToken);
        }

        // Tương ứng index unique partial: mỗi user chỉ có một gói Active
        public Task<bool> OtherActiveSubscriptionExistsAsync(Guid userId, Guid excludeId, CancellationToken cancellationToken = default)
        {
            return _context.UserSubscriptions.AnyAsync(
                s => s.UserId == userId && s.Status == ActiveStatus && s.Id != excludeId, cancellationToken);
        }

        public Task<bool> HasAiUsageAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
        {
            return _context.AiusageLogs.AnyAsync(l => l.UserSubscriptionId == subscriptionId, cancellationToken);
        }

        public async Task AddAsync(UserSubscription subscription, CancellationToken cancellationToken = default)
        {
            await _context.UserSubscriptions.AddAsync(subscription, cancellationToken);
        }

        public void Remove(UserSubscription subscription)
        {
            _context.UserSubscriptions.Remove(subscription);
        }
    }
}
