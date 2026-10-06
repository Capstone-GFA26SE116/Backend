using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IMilestoneService
    {
        Task<IReadOnlyList<MilestoneDto>> GetMilestonesAsync(Guid userId, Guid projectId, CancellationToken cancellationToken = default);

        Task<MilestoneDto> GetMilestoneAsync(Guid userId, Guid milestoneId, CancellationToken cancellationToken = default);

        Task<MilestoneDto> CreateMilestoneAsync(Guid userId, Guid projectId, MilestoneRequestDto request, CancellationToken cancellationToken = default);

        Task<MilestoneDto> UpdateMilestoneAsync(Guid userId, Guid milestoneId, MilestoneRequestDto request, CancellationToken cancellationToken = default);

        Task DeleteMilestoneAsync(Guid userId, Guid milestoneId, CancellationToken cancellationToken = default);
    }
}
