using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Logging;

namespace METANOIA.Infrastructure.Services
{
    public class ScheduleProposalService : IScheduleProposalService
    {
        private const string PendingStatus = "Pending";
        private const string ApprovedStatus = "Approved";
        private const string ProposedSlotStatus = "Proposed";
        private const string ApprovedSlotStatus = "Approved";
        private const string CancelledSlotStatus = "Cancelled";

        private readonly IAiServiceClient _aiServiceClient;
        private readonly AppDbContext _context;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IScheduleJobService _jobService;
        private readonly ITaskProgressService _progress;
        private readonly ICalendarRepository _calendar;
        private readonly IGoogleCalendarSyncService _googleSync;
        private readonly GoogleSyncGate _syncGate;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<ScheduleProposalService> _logger;

        public ScheduleProposalService(
            IAiServiceClient aiServiceClient,
            AppDbContext context,
            IUnitOfWork unitOfWork,
            IScheduleJobService jobService,
            ITaskProgressService progress,
            ICalendarRepository calendar,
            IGoogleCalendarSyncService googleSync,
            GoogleSyncGate syncGate,
            TimeProvider timeProvider,
            ILogger<ScheduleProposalService> logger)
        {
            _aiServiceClient = aiServiceClient;
            _context = context;
            _unitOfWork = unitOfWork;
            _jobService = jobService;
            _progress = progress;
            _calendar = calendar;
            _googleSync = googleSync;
            _syncGate = syncGate;
            _timeProvider = timeProvider;
            _logger = logger;
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

        // ---------------------------------------------------------------- Duyệt

        public async Task<ScheduleProposalDto> ApproveProposalAsync(Guid userId, Guid proposalId, bool skipConflicts, CancellationToken cancellationToken = default)
        {
            var proposal = await LoadProposalAsync(userId, proposalId, cancellationToken);

            if (proposal.Status != PendingStatus)
            {
                throw new UserFriendlyException($"Bản đề xuất lịch đang ở trạng thái '{proposal.Status}', chỉ có thể duyệt đề xuất 'Pending'.");
            }

            if (!proposal.ScheduleSlots.Any(s => s.Status == ProposedSlotStatus))
            {
                throw new UserFriendlyException("Không còn slot nào để duyệt (mọi slot đã bị từ chối hoặc đã xử lý).");
            }

            // Kiểm lại Google Calendar TRƯỚC khi đổi bất cứ thứ gì (việc đồng bộ có thể lưu dữ liệu Google, không đụng tới đề xuất)
            var googleCheck = await RefreshGoogleAsync(userId, cancellationToken);

            var proposedSlots = proposal.ScheduleSlots.Where(s => s.Status == ProposedSlotStatus).ToList();
            var conflicts = googleCheck == "NotConnected"
                ? new List<SlotConflictDto>()
                : await FindConflictsAsync(userId, proposedSlots, cancellationToken);

            if (conflicts.Count > 0 && !skipConflicts)
            {
                throw new ScheduleConflictException(
                    $"{conflicts.Count} slot trùng với sự kiện trên Google Calendar. Hãy sửa giờ hoặc từ chối các slot đó, " +
                    "hoặc duyệt với skipConflicts=true để bỏ các slot trùng và duyệt phần còn lại.",
                    new { conflicts });
            }

            var conflictIds = conflicts.Select(c => c.SlotId).ToHashSet();
            var toApprove = proposedSlots.Where(s => !conflictIds.Contains(s.Id)).ToList();
            if (toApprove.Count == 0)
            {
                throw new UserFriendlyException("Mọi slot còn lại đều trùng Google Calendar nên không còn slot nào để duyệt. Hãy sửa giờ các slot đó trước.");
            }

            proposal.Status = ApprovedStatus;
            var now = DateTime.UtcNow;

            // Slot trùng Google (khi người dùng chọn bỏ): không ghi, chuyển Cancelled
            foreach (var slot in proposedSlots.Where(s => conflictIds.Contains(s.Id)))
            {
                var oldStart = slot.StartTime;
                var oldEnd = slot.EndTime;
                slot.Status = CancelledSlotStatus;
                _context.ScheduleChangeLogs.Add(NewLog(slot.Id, "Deleted", "GoogleSync", oldStart, oldEnd, null, null, now));
            }

            foreach (var slot in toApprove)
            {
                slot.Status = ApprovedSlotStatus;
                _context.ScheduleChangeLogs.Add(NewLog(slot.Id, "Created", "AI", null, null, slot.StartTime, slot.EndTime, now));
            }

            var result = new ProgressResult();
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Slot vừa được duyệt đưa Task từ ToDo lên Scheduled
                foreach (var taskId in toApprove.Select(s => s.TaskId).Distinct())
                {
                    result.Merge(await _progress.RefreshTaskAsync(taskId, cancellationToken));
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }, cancellationToken);

            // Slot vừa được duyệt thì mới hẹn các job nhắc việc (nhắc trước giờ, đến giờ bấm Start, quá giờ...)
            foreach (var slot in toApprove)
            {
                _jobService.ScheduleAllJobsForSlot(slot);
            }

            await _progress.AfterCommitAsync(result, cancellationToken);

            var dto = ToDto(proposal);
            dto.GoogleCheck = googleCheck;
            dto.SkippedConflicts = conflicts;
            return dto;
        }

        // ---------------------------------------------------------------- Sửa và từ chối từng slot

        public async Task<ProposedSlotUpdateResultDto> UpdateProposedSlotAsync(
            Guid userId, Guid proposalId, Guid slotId, ProposedSlotUpdateRequest request, CancellationToken cancellationToken = default)
        {
            var (_, slot) = await LoadEditableSlotAsync(userId, proposalId, slotId, cancellationToken);

            if (request.EndTime <= request.StartTime)
            {
                throw new UserFriendlyException("Giờ kết thúc phải lớn hơn giờ bắt đầu.");
            }

            if (request.StartTime <= _timeProvider.GetUtcNow())
            {
                throw new UserFriendlyException("Giờ bắt đầu phải ở tương lai.");
            }

            var newStart = UtcTime.ToStorage(request.StartTime);
            var newEnd = UtcTime.ToStorage(request.EndTime);
            var oldStart = slot.StartTime;
            var oldEnd = slot.EndTime;

            if (newStart != oldStart || newEnd != oldEnd)
            {
                if (newStart != oldStart && slot.OriginalStartTime is null)
                {
                    slot.OriginalStartTime = oldStart;
                }

                slot.StartTime = newStart;
                slot.EndTime = newEnd;
                slot.ProposedMinutes = (int)Math.Round((request.EndTime - request.StartTime).TotalMinutes);
                slot.IsManualOverride = true;

                _context.ScheduleChangeLogs.Add(NewLog(
                    slot.Id, newStart != oldStart ? "Moved" : "Resized", "Manual", oldStart, oldEnd, newStart, newEnd, DateTime.UtcNow));
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return new ProposedSlotUpdateResultDto
            {
                Slot = ToSlotDto(slot),
                GoogleConflicts = await FindConflictsAsync(userId, new[] { slot }, cancellationToken)
            };
        }

        public async Task<ScheduleSlotDto> RejectProposedSlotAsync(Guid userId, Guid proposalId, Guid slotId, CancellationToken cancellationToken = default)
        {
            var (_, slot) = await LoadEditableSlotAsync(userId, proposalId, slotId, cancellationToken);

            StatusRules.EnsureSlot(slot.Status, CancelledSlotStatus);

            var oldStart = slot.StartTime;
            var oldEnd = slot.EndTime;
            slot.Status = CancelledSlotStatus;
            _context.ScheduleChangeLogs.Add(NewLog(slot.Id, "Deleted", "Manual", oldStart, oldEnd, null, null, DateTime.UtcNow));
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToSlotDto(slot);
        }

        public async Task<ProposalConflictsDto> GetConflictsAsync(Guid userId, Guid proposalId, CancellationToken cancellationToken = default)
        {
            var proposal = await LoadProposalAsync(userId, proposalId, cancellationToken);

            if (!await HasActiveGoogleConnectionAsync(userId, cancellationToken))
            {
                return new ProposalConflictsDto { GoogleCheck = "NotConnected" };
            }

            return new ProposalConflictsDto
            {
                GoogleCheck = "Cached",
                Conflicts = await FindConflictsAsync(userId, proposal.ScheduleSlots.Where(s => s.Status == ProposedSlotStatus).ToList(), cancellationToken)
            };
        }

        // ---------------------------------------------------------------- Dùng chung

        private async Task<ScheduleProposal> LoadProposalAsync(Guid userId, Guid proposalId, CancellationToken cancellationToken)
        {
            return await _context.ScheduleProposals
                       .Include(p => p.ScheduleSlots).ThenInclude(s => s.Task)
                       .FirstOrDefaultAsync(p => p.Id == proposalId && p.UserId == userId, cancellationToken)
                   ?? throw new NotFoundException("Không tìm thấy bản đề xuất lịch.");
        }

        // Slot của đề xuất còn Pending và chính slot còn Proposed: điều kiện để sửa hoặc từ chối
        private async Task<(ScheduleProposal Proposal, ScheduleSlot Slot)> LoadEditableSlotAsync(
            Guid userId, Guid proposalId, Guid slotId, CancellationToken cancellationToken)
        {
            var proposal = await LoadProposalAsync(userId, proposalId, cancellationToken);

            if (proposal.Status != PendingStatus)
            {
                throw new UserFriendlyException($"Bản đề xuất lịch đang ở trạng thái '{proposal.Status}', chỉ sửa hoặc từ chối được slot của đề xuất 'Pending'.");
            }

            var slot = proposal.ScheduleSlots.FirstOrDefault(s => s.Id == slotId)
                ?? throw new NotFoundException("Không tìm thấy slot trong bản đề xuất này.");

            if (slot.Status != ProposedSlotStatus)
            {
                throw new UserFriendlyException($"Slot đang ở trạng thái '{slot.Status}', chỉ sửa hoặc từ chối được slot 'Proposed'.");
            }

            return (proposal, slot);
        }

        private async Task<bool> HasActiveGoogleConnectionAsync(Guid userId, CancellationToken cancellationToken)
        {
            return await _context.GoogleConnections.AnyAsync(c => c.UserId == userId && c.Status == "Active", cancellationToken);
        }

        // Đồng bộ gia tăng với Google ngay lúc duyệt để dữ liệu mới nhất. Lỗi đồng bộ không chặn duyệt (kiểm theo dữ liệu đã lưu)
        private async Task<string> RefreshGoogleAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (!await HasActiveGoogleConnectionAsync(userId, cancellationToken))
            {
                return "NotConnected";
            }

            try
            {
                await _syncGate.RunAsync(userId, () => _googleSync.SyncAsync(userId, cancellationToken));
                return "Checked";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Không đồng bộ được Google Calendar lúc duyệt đề xuất của user {UserId}; kiểm theo dữ liệu đã lưu.", userId);
                return "Stale";
            }
        }

        // Slot trùng sự kiện Google (giao nhau, chạm đầu mút không tính). Bỏ qua sự kiện cả ngày; sự kiện đã hủy và sự kiện do
        // METANOIA tự đẩy lên đã được repository loại sẵn
        private async Task<List<SlotConflictDto>> FindConflictsAsync(Guid userId, IReadOnlyCollection<ScheduleSlot> slots, CancellationToken cancellationToken)
        {
            if (slots.Count == 0)
            {
                return new List<SlotConflictDto>();
            }

            var from = slots.Min(s => s.StartTime);
            var to = slots.Max(s => s.EndTime);
            var events = (await _calendar.GetGoogleEventsAsync(userId, from, to, cancellationToken))
                .Where(e => !e.IsAllDay)
                .ToList();

            var conflicts = new List<SlotConflictDto>();
            foreach (var slot in slots.OrderBy(s => s.StartTime))
            {
                var hit = events.Where(e => e.StartTime < slot.EndTime && e.EndTime > slot.StartTime).OrderBy(e => e.StartTime).FirstOrDefault();
                if (hit is null)
                {
                    continue;
                }

                conflicts.Add(new SlotConflictDto(
                    slot.Id,
                    slot.TaskId,
                    slot.Task?.Title ?? string.Empty,
                    UtcTime.ToApi(slot.StartTime),
                    UtcTime.ToApi(slot.EndTime),
                    new GoogleEventBriefDto(hit.Title, UtcTime.ToApi(hit.StartTime), UtcTime.ToApi(hit.EndTime))));
            }

            return conflicts;
        }

        private static ScheduleChangeLog NewLog(
            Guid slotId, string changeType, string source, DateTime? oldStart, DateTime? oldEnd, DateTime? newStart, DateTime? newEnd, DateTime at)
        {
            return new ScheduleChangeLog
            {
                Id = Guid.NewGuid(),
                ScheduleSlotId = slotId,
                ChangeType = changeType,
                Source = source,
                OldStart = oldStart,
                OldEnd = oldEnd,
                NewStart = newStart,
                NewEnd = newEnd,
                ChangedAt = at
            };
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
                Slots = proposal.ScheduleSlots.Select(ToSlotDto).ToList()
            };
        }

        private static ScheduleSlotDto ToSlotDto(ScheduleSlot s)
        {
            return new ScheduleSlotDto
            {
                Id = s.Id,
                TaskId = s.TaskId,
                SubTaskId = s.SubTaskId,
                ScheduleProposalId = s.ScheduleProposalId,
                StartTime = UtcTime.ToApi(s.StartTime),
                EndTime = UtcTime.ToApi(s.EndTime),
                ProposedMinutes = s.ProposedMinutes,
                Explanation = s.Explanation,
                Status = s.Status,
                OriginalStartTime = UtcTime.ToApi(s.OriginalStartTime),
                IsManualOverride = s.IsManualOverride,
                GoogleEventId = s.GoogleEventId,
                SyncFailed = s.SyncFailed
            };
        }
    }
}
