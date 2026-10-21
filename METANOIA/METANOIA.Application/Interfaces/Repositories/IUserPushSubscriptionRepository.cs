using UserPushSubscription = METANOIA.Domain.Entities.UserPushSubscription;

namespace METANOIA.Application.Interfaces.Repositories;

public interface IUserPushSubscriptionRepository
{
    Task<UserPushSubscription?> GetByEndpointAsync(string endpoint);
    Task<UserPushSubscription?> GetByUserAndEndpointAsync(Guid userId, string endpoint);
    Task<IReadOnlyList<UserPushSubscription>> GetByUserIdAsync(Guid userId);
    Task AddAsync(UserPushSubscription subscription);
    void Remove(UserPushSubscription subscription);
}
