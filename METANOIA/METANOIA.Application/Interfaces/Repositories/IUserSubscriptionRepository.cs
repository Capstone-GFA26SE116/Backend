using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IUserSubscriptionRepository
    {
        Task<IReadOnlyList<UserSubscription>> GetAsync(Guid? userId, CancellationToken cancellationToken = default);

        Task<UserSubscription?> GetByIdAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

        Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<bool> PlanExistsAsync(Guid planId, CancellationToken cancellationToken = default);

        Task<bool> PaymentExistsAsync(Guid paymentId, CancellationToken cancellationToken = default);

        Task<bool> PaymentLinkedToOtherSubscriptionAsync(Guid paymentId, Guid excludeId, CancellationToken cancellationToken = default);

        Task<bool> OtherActiveSubscriptionExistsAsync(Guid userId, Guid excludeId, CancellationToken cancellationToken = default);

        Task<bool> HasAiUsageAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

        Task<UserSubscription?> GetActiveByUserAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<UserSubscription?> GetByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default);

        // Gói Scheduled đầu tiên của user đã đến ngày bắt đầu
        Task<UserSubscription?> GetDueScheduledForUserAsync(Guid userId, DateTime now, CancellationToken cancellationToken = default);

        // Gói Active đã quá EndAt
        Task<IReadOnlyList<UserSubscription>> GetExpiredActiveAsync(DateTime now, int take, CancellationToken cancellationToken = default);

        Task AddAsync(UserSubscription subscription, CancellationToken cancellationToken = default);

        void Remove(UserSubscription subscription);
    }
}
