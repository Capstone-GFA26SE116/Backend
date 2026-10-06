using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class SubscriptionPlanRepository : ISubscriptionPlanRepository
    {
        private readonly AppDbContext _context;

        public SubscriptionPlanRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<SubscriptionPlan>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _context.SubscriptionPlans.OrderBy(p => p.Price).ToListAsync(cancellationToken);
        }

        public Task<SubscriptionPlan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken = default)
        {
            return _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        }

        public Task<bool> NameExistsAsync(string name, Guid excludeId, CancellationToken cancellationToken = default)
        {
            var normalized = name.ToLower();
            return _context.SubscriptionPlans.AnyAsync(p => p.Id != excludeId && p.Name.ToLower() == normalized, cancellationToken);
        }

        // UserSubscription và PaymentTransaction đều tham chiếu plan với RESTRICT
        public async Task<bool> IsInUseAsync(Guid planId, CancellationToken cancellationToken = default)
        {
            return await _context.UserSubscriptions.AnyAsync(s => s.PlanId == planId, cancellationToken)
                || await _context.PaymentTransactions.AnyAsync(t => t.PlanId == planId, cancellationToken);
        }

        public async Task AddAsync(SubscriptionPlan plan, CancellationToken cancellationToken = default)
        {
            await _context.SubscriptionPlans.AddAsync(plan, cancellationToken);
        }

        public void Remove(SubscriptionPlan plan)
        {
            _context.SubscriptionPlans.Remove(plan);
        }
    }
}
