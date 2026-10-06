using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IUserSubscriptionService
    {
        // Người dùng thường chỉ thấy gói của chính mình; Admin thấy tất cả
        Task<IReadOnlyList<UserSubscriptionDto>> GetSubscriptionsAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken = default);

        Task<UserSubscriptionDto> GetSubscriptionAsync(Guid userId, bool isAdmin, Guid subscriptionId, CancellationToken cancellationToken = default);

        Task<UserSubscriptionDto> CreateSubscriptionAsync(UserSubscriptionRequestDto request, CancellationToken cancellationToken = default);

        Task<UserSubscriptionDto> UpdateSubscriptionAsync(Guid subscriptionId, UserSubscriptionRequestDto request, CancellationToken cancellationToken = default);

        Task DeleteSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
    }
}
