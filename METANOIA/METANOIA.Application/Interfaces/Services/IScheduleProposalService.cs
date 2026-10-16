using System;
using System.Threading;
using System.Threading.Tasks;
using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IScheduleProposalService
    {
        Task<AiScheduleProposalResponseDto> ProposeScheduleAsync(Guid userId, ScheduleProposalRequestDto request, CancellationToken cancellationToken = default);

        Task<ScheduleProposalDto?> GetPendingProposalAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<ScheduleProposalDto> ApproveProposalAsync(Guid userId, Guid proposalId, CancellationToken cancellationToken = default);
    }
}
