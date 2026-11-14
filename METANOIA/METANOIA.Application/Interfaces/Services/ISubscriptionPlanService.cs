using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface ISubscriptionPlanService
    {
        // includeInactive = false: chỉ gói đang bán (cho người dùng thường); Admin truyền true để thấy cả gói đã tắt
        Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(bool includeInactive, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default);

        Task<SubscriptionPlanDto> GetPlanAsync(Guid planId, CancellationToken cancellationToken = default);

        Task<SubscriptionPlanDto> CreatePlanAsync(SubscriptionPlanRequestDto request, CancellationToken cancellationToken = default);

        Task<SubscriptionPlanDto> UpdatePlanAsync(Guid planId, SubscriptionPlanRequestDto request, CancellationToken cancellationToken = default);

        Task DeletePlanAsync(Guid planId, CancellationToken cancellationToken = default);
    }
}
