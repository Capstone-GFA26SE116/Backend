using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using UserPushSubscription = METANOIA.Domain.Entities.UserPushSubscription;

namespace METANOIA.Infrastructure.Services;

public sealed class PushSubscriptionService(
    IUserPushSubscriptionRepository subscriptionRepository,
    IUnitOfWork unitOfWork) : IPushSubscriptionService
{
    public async Task SubscribeAsync(Guid userId, PushSubscriptionDto request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Endpoint)
            || request.Keys is null
            || string.IsNullOrWhiteSpace(request.Keys.P256dh)
            || string.IsNullOrWhiteSpace(request.Keys.Auth))
        {
            throw new ArgumentException("A valid push subscription is required.", nameof(request));
        }

        var subscription = await subscriptionRepository.GetByEndpointAsync(request.Endpoint);
        if (subscription is null)
        {
            await subscriptionRepository.AddAsync(new UserPushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Endpoint = request.Endpoint,
                P256dh = request.Keys.P256dh,
                Auth = request.Keys.Auth,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            subscription.UserId = userId;
            subscription.P256dh = request.Keys.P256dh;
            subscription.Auth = request.Keys.Auth;
        }

        await unitOfWork.SaveChangesAsync();
    }

    public async Task UnsubscribeAsync(Guid userId, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return;

        var subscription = await subscriptionRepository.GetByUserAndEndpointAsync(userId, endpoint);
        if (subscription is null)
            return;

        subscriptionRepository.Remove(subscription);
        await unitOfWork.SaveChangesAsync();
    }
}
