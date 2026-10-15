using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace METANOIA.Infrastructure.Services
{
    public class ScheduleProposalService : IScheduleProposalService
    {
        private const string PendingStatus = "Pending";
        private const string ApprovedStatus = "Approved";
        private const string ProposedSlotStatus = "Proposed";
        private const string ApprovedSlotStatus = "Approved";

        private readonly IAiServiceClient _aiServiceClient;
        private readonly AppDbContext _context;
        private readonly IUnitOfWork _unitOfWork;

        public ScheduleProposalService(
            IAiServiceClient aiServiceClient,
            AppDbContext context,
            IUnitOfWork unitOfWork)
        {
            _aiServiceClient = aiServiceClient;
            _context = context;
            _unitOfWork = unitOfWork;
        }

        public async Task<AiScheduleProposalResponseDto> ProposeScheduleAsync(
            Guid userId,
            ScheduleProposalRequestDto request,
            CancellationToken cancellationToken = default)
        {
            var aiRequest = new AiScheduleProposeRequestDto
            {
                TaskIds = request.TaskIds,
                Language = string.IsNullOrWhiteSpace(request.Language) ? "vi" : request.Language
            };

            return await _aiServiceClient.ProposeScheduleAsync(aiRequest, cancellationToken);
        }

        public async Task<ScheduleProposalDto?> GetPendingProposalAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var proposal = await _context.ScheduleProposals
                .AsNoTracking()
                .Include(p => p.ScheduleSlots.Where(s => s.Status == ProposedSlotStatus))
                .Where(p => p.UserId == userId && p.Status == PendingStatus)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (proposal is null)
            {
                return null;
            }

            return ToDto(proposal);
        }

        public async Task<ScheduleProposalDto> ApproveProposalAsync(Guid userId, Guid proposalId, CancellationToken cancellationToken = default)
        {
            var proposal = await _context.ScheduleProposals
                .Include(p => p.ScheduleSlots)
                .FirstOrDefaultAsync(p => p.Id == proposalId && p.UserId == userId, cancellationToken);

            if (proposal is null)
            {
                throw new NotFoundException("Không tìm thấy bản đề xuất lịch.");
            }

            if (proposal.Status != PendingStatus)
            {
                throw new UserFriendlyException($"Bản đề xuất lịch đang ở trạng thái '{proposal.Status}', chỉ có thể duyệt đề xuất 'Pending'.");
            }

            proposal.Status = ApprovedStatus;

            var proposedSlots = proposal.ScheduleSlots.Where(s => s.Status == ProposedSlotStatus).ToList();
            var now = DateTime.UtcNow;

            foreach (var slot in proposedSlots)
            {
                slot.Status = ApprovedSlotStatus;

                var changeLog = new ScheduleChangeLog
                {
                    Id = Guid.NewGuid(),
                    ScheduleSlotId = slot.Id,
                    ChangeType = "Approved",
                    Source = "User",
                    NewStart = slot.StartTime,
                    NewEnd = slot.EndTime,
                    ChangedAt = now
                };

                await _context.ScheduleChangeLogs.AddAsync(changeLog, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(proposal);
        }

        private static ScheduleProposalDto ToDto(ScheduleProposal proposal)
        {
            return new ScheduleProposalDto
            {
                Id = proposal.Id,
                UserId = proposal.UserId,
                Status = proposal.Status,
                IsFeasible = proposal.IsFeasible,
                MissingMinutes = proposal.MissingMinutes,
                Explanation = proposal.Explanation,
                CreatedAt = proposal.CreatedAt,
                Slots = proposal.ScheduleSlots.Select(s => new ScheduleSlotDto
                {
                    Id = s.Id,
                    TaskId = s.TaskId,
                    SubTaskId = s.SubTaskId,
                    ScheduleProposalId = s.ScheduleProposalId,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    ProposedMinutes = s.ProposedMinutes,
                    Explanation = s.Explanation,
                    Status = s.Status,
                    OriginalStartTime = s.OriginalStartTime,
                    IsManualOverride = s.IsManualOverride,
                    GoogleEventId = s.GoogleEventId,
                    SyncFailed = s.SyncFailed
                }).ToList()
            };
        }
    }
}
