using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class ScheduleSlotService : IScheduleSlotService
    {
        private const string ProposedStatus = "Proposed";
        private const string CancelledStatus = "Cancelled";
        private const string ManualSource = "Manual";
        private const int MaxExplanationLength = 500;

        private static readonly HashSet<string> AllowedStatuses = new()
        {
            ProposedStatus, "Approved", "Done", "Skipped", "Missed", CancelledStatus
        };

        private readonly IScheduleSlotRepository _slotRepository;
        private readonly IScheduleChangeLogRepository _logRepository;
        private readonly IUnitOfWork _unitOfWork;

        public ScheduleSlotService(
            IScheduleSlotRepository slotRepository,
            IScheduleChangeLogRepository logRepository,
            IUnitOfWork unitOfWork)
        {
            _slotRepository = slotRepository;
            _logRepository = logRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<ScheduleSlotDto>> GetSlotsAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default)
        {
            await EnsureTaskOwnedAsync(userId, taskId, cancellationToken);

            var slots = await _slotRepository.GetByTaskIdAsync(taskId, cancellationToken);
            return slots.Select(ToDto).ToList();
        }

        public async Task<ScheduleSlotDto> GetSlotAsync(Guid userId, Guid slotId, CancellationToken cancellationToken = default)
        {
            var slot = await GetOwnedSlotAsync(userId, slotId, cancellationToken);
            return ToDto(slot);
        }

        public async Task<ScheduleSlotDto> CreateSlotAsync(Guid userId, Guid taskId, ScheduleSlotRequestDto request, CancellationToken cancellationToken = default)
        {
            await EnsureTaskOwnedAsync(userId, taskId, cancellationToken);
            ValidateRequest(request);
            await ValidateReferencesAsync(userId, taskId, request, cancellationToken);

            var slot = new ScheduleSlot
            {
                Id = Guid.NewGuid(),
                TaskId = taskId,
                Status = ProposedStatus
            };
            ApplyRequest(slot, request);

            await _slotRepository.AddAsync(slot, cancellationToken);
            await AddLogAsync(slot.Id, "Created", null, null, slot.StartTime, slot.EndTime, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(slot);
        }

        public async Task<ScheduleSlotDto> UpdateSlotAsync(Guid userId, Guid slotId, ScheduleSlotRequestDto request, CancellationToken cancellationToken = default)
        {
            var slot = await GetOwnedSlotAsync(userId, slotId, cancellationToken);
            ValidateRequest(request);
            await ValidateReferencesAsync(userId, slot.TaskId, request, cancellationToken);

            var oldStart = slot.StartTime;
            var oldEnd = slot.EndTime;
            ApplyRequest(slot, request);

            var startChanged = slot.StartTime != oldStart;
            var endChanged = slot.EndTime != oldEnd;
            if (startChanged || endChanged)
            {
                if (startChanged && slot.OriginalStartTime is null)
                {
                    slot.OriginalStartTime = oldStart;
                }

                var changeType = startChanged ? "Moved" : "Resized";
                await AddLogAsync(slot.Id, changeType, oldStart, oldEnd, slot.StartTime, slot.EndTime, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(slot);
        }

        public async Task<ScheduleSlotDto> CancelSlotAsync(Guid userId, Guid slotId, CancellationToken cancellationToken = default)
        {
            var slot = await GetOwnedSlotAsync(userId, slotId, cancellationToken);
            if (slot.Status == CancelledStatus)
            {
                throw new UserFriendlyException("Slot này đã bị hủy trước đó.");
            }

            var oldStart = slot.StartTime;
            var oldEnd = slot.EndTime;
            slot.Status = CancelledStatus;

            await AddLogAsync(slot.Id, "Deleted", oldStart, oldEnd, null, null, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(slot);
        }

        private async Task EnsureTaskOwnedAsync(Guid userId, Guid taskId, CancellationToken cancellationToken)
        {
            if (!await _slotRepository.TaskOwnedByUserAsync(taskId, userId, cancellationToken))
            {
                throw new NotFoundException("Không tìm thấy task.");
            }
        }

        private async Task<ScheduleSlot> GetOwnedSlotAsync(Guid userId, Guid slotId, CancellationToken cancellationToken)
        {
            return await _slotRepository.GetByIdForUserAsync(slotId, userId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy slot.");
        }

        private async Task ValidateReferencesAsync(Guid userId, Guid taskId, ScheduleSlotRequestDto request, CancellationToken cancellationToken)
        {
            if (request.SubTaskId is { } subTaskId
                && !await _slotRepository.SubTaskBelongsToTaskAsync(subTaskId, taskId, cancellationToken))
            {
                throw new UserFriendlyException("Subtask không tồn tại trong task này.");
            }

            if (request.ScheduleProposalId is { } proposalId
                && !await _slotRepository.ProposalOwnedByUserAsync(proposalId, userId, cancellationToken))
            {
                throw new UserFriendlyException("Đề xuất lịch không tồn tại.");
            }
        }

        private static void ValidateRequest(ScheduleSlotRequestDto request)
        {
            if (request.EndTime <= request.StartTime)
            {
                throw new UserFriendlyException("EndTime phải lớn hơn StartTime.");
            }

            if (request.Status is not null && !AllowedStatuses.Contains(request.Status))
            {
                throw new UserFriendlyException("Trạng thái không hợp lệ. Chọn một trong: Proposed, Approved, Done, Skipped, Missed, Cancelled.");
            }

            if (request.Explanation?.Length > MaxExplanationLength)
            {
                throw new UserFriendlyException($"Giải thích tối đa {MaxExplanationLength} ký tự.");
            }

            // numeric(5,3): phần nguyên tối đa 2 chữ số
            if (request.KbiasUsed is { } kbias && Math.Abs(kbias) >= 100)
            {
                throw new UserFriendlyException("KBiasUsed vượt giới hạn cho phép.");
            }
        }

        private static void ApplyRequest(ScheduleSlot slot, ScheduleSlotRequestDto request)
        {
            slot.SubTaskId = request.SubTaskId;
            slot.ScheduleProposalId = request.ScheduleProposalId;
            slot.StartTime = UtcTime.ToStorage(request.StartTime);
            slot.EndTime = UtcTime.ToStorage(request.EndTime);
            slot.ProposedMinutes = (int)Math.Round((request.EndTime - request.StartTime).TotalMinutes);
            slot.OriginalStartTime = request.OriginalStartTime is { } original
                ? UtcTime.ToStorage(original)
                : slot.OriginalStartTime;
            slot.IsManualOverride = request.IsManualOverride;
            slot.KbiasUsed = request.KbiasUsed;
            slot.Explanation = request.Explanation;
            slot.Status = string.IsNullOrWhiteSpace(request.Status) ? slot.Status : request.Status;
        }

        private Task AddLogAsync(Guid slotId, string changeType, DateTime? oldStart, DateTime? oldEnd, DateTime? newStart, DateTime? newEnd, CancellationToken cancellationToken)
        {
            return _logRepository.AddAsync(new ScheduleChangeLog
            {
                Id = Guid.NewGuid(),
                ScheduleSlotId = slotId,
                ChangeType = changeType,
                Source = ManualSource,
                OldStart = oldStart,
                OldEnd = oldEnd,
                NewStart = newStart,
                NewEnd = newEnd,
                ChangedAt = DateTime.UtcNow
            }, cancellationToken);
        }

        private static ScheduleSlotDto ToDto(ScheduleSlot slot)
        {
            return new ScheduleSlotDto
            {
                Id = slot.Id,
                TaskId = slot.TaskId,
                SubTaskId = slot.SubTaskId,
                ScheduleProposalId = slot.ScheduleProposalId,
                StartTime = UtcTime.ToApi(slot.StartTime),
                EndTime = UtcTime.ToApi(slot.EndTime),
                OriginalStartTime = UtcTime.ToApi(slot.OriginalStartTime),
                IsManualOverride = slot.IsManualOverride,
                ProposedMinutes = slot.ProposedMinutes,
                KbiasUsed = slot.KbiasUsed,
                Explanation = slot.Explanation,
                Status = slot.Status,
                GoogleEventId = slot.GoogleEventId,
                SyncFailed = slot.SyncFailed
            };
        }
    }
}
