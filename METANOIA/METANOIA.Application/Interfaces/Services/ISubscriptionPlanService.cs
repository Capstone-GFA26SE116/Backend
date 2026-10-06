using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface ISubscriptionPlanService
    {
        Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default);

        Task<SubscriptionPlanDto> GetPlanAsync(Guid planId, CancellationToken cancellationToken = default);

        Task<SubscriptionPlanDto> CreatePlanAsync(SubscriptionPlanRequestDto request, CancellationToken cancellationToken = default);

        Task<SubscriptionPlanDto> UpdatePlanAsync(Guid planId, SubscriptionPlanRequestDto request, CancellationToken cancellationToken = default);

        Task DeletePlanAsync(Guid planId, CancellationToken cancellationToken = default);
    }
}
