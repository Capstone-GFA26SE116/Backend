using METANOIA.Application.Interfaces.Repositories;
using UserPushSubscription = METANOIA.Domain.Entities.UserPushSubscription;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace METANOIA.Infrastructure.Repositories;

public sealed class UserPushSubscriptionRepository(AppDbContext dbContext) : IUserPushSubscriptionRepository
{
    public Task<UserPushSubscription?> GetByEndpointAsync(string endpoint)
    {
        return dbContext.UserPushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint);
    }

    public Task<UserPushSubscription?> GetByUserAndEndpointAsync(Guid userId, string endpoint)
    {
        return dbContext.UserPushSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == endpoint);
    }

    public async Task<IReadOnlyList<UserPushSubscription>> GetByUserIdAsync(Guid userId)
    {
        return await dbContext.UserPushSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync();
    }

    public Task AddAsync(UserPushSubscription subscription)
    {
        return dbContext.UserPushSubscriptions.AddAsync(subscription).AsTask();
    }

    public void Remove(UserPushSubscription subscription)
    {
        dbContext.UserPushSubscriptions.Remove(subscription);
    }
}
