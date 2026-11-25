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
            ProposedStatus, "Approved", "Due", "InProgress", "Done", "Skipped",
            "Missed", "Overrun", "Postponed", CancelledStatus
        };

        private readonly IScheduleSlotRepository _slotRepository;
        private readonly IScheduleChangeLogRepository _logRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IScheduleJobService _jobService;
        private readonly ITaskProgressService _progress;
        private readonly IGroupScheduleService _groupSchedule;

        public ScheduleSlotService(
            IScheduleSlotRepository slotRepository,
            IScheduleChangeLogRepository logRepository,
            IUnitOfWork unitOfWork,
            IScheduleJobService jobService,
            ITaskProgressService progress,
            IGroupScheduleService groupSchedule)
        {
            _slotRepository = slotRepository;
            _logRepository = logRepository;
            _unitOfWork = unitOfWork;
            _jobService = jobService;
            _progress = progress;
            _groupSchedule = groupSchedule;
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

            // Task đã Done hoặc Skipped thì không xếp thêm lịch (muốn làm tiếp phải mở lại task)
            var taskStatus = await _slotRepository.GetTaskStatusAsync(taskId, cancellationToken);
            if (taskStatus is not null && StatusRules.IsTerminalTask(taskStatus))
            {
                throw new UserFriendlyException($"Task đang ở trạng thái '{taskStatus}' nên không xếp thêm lịch. Hãy mở lại task trước.");
            }

            var slot = new ScheduleSlot
            {
                Id = Guid.NewGuid(),
                TaskId = taskId,
                Status = ProposedStatus
            };
            ApplyRequest(slot, request);

            // Slot mới chỉ bắt đầu ở Proposed (chưa duyệt) hoặc Approved (đã duyệt luôn); các trạng thái sau do luồng làm việc quyết định
            if (slot.Status is not (ProposedStatus or "Approved"))
            {
                throw new UserFriendlyException("Slot mới chỉ tạo được ở trạng thái Proposed hoặc Approved.");
            }

            // Slot đã duyệt thì chiếm giờ: không được chồng lên slot đang hoạt động khác (Proposed chưa chiếm giờ, kiểm khi duyệt)
            await EnsureNoOverlapAsync(userId, slot, cancellationToken);

            var result = new ProgressResult { TaskId = taskId };
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await _slotRepository.AddAsync(slot, cancellationToken);
                await AddLogAsync(slot.Id, "Created", null, null, slot.StartTime, slot.EndTime, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Slot sống đầu tiên đưa Task từ ToDo lên Scheduled
                result.Merge(await _progress.RefreshTaskAsync(taskId, cancellationToken));
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }, cancellationToken);

            SyncJobs(slot);
            await _progress.AfterCommitAsync(result, cancellationToken);

            return ToDto(slot);
        }

        public async Task<ScheduleSlotDto> UpdateSlotAsync(Guid userId, Guid slotId, ScheduleSlotRequestDto request, CancellationToken cancellationToken = default)
        {
            var slot = await GetOwnedSlotAsync(userId, slotId, cancellationToken);

            // Slot của việc nhóm: luật riêng (quyền theo vai trò trong nhóm, không có việc cá nhân để suy lại trạng thái)
            if (slot.GroupTaskId is not null)
            {
                return await _groupSchedule.UpdateSlotAsync(userId, slot, request, cancellationToken);
            }

            ValidateRequest(request);
            await ValidateReferencesAsync(userId, slot.TaskId!.Value, request, cancellationToken);

            // Chuyển trạng thái phải theo bảng luật (không gửi status thì giữ nguyên)
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                StatusRules.EnsureSlot(slot.Status, request.Status);
            }

            var oldStart = slot.StartTime;
            var oldEnd = slot.EndTime;
            var wasLive = StatusRules.IsLiveSlot(slot.Status);
            ApplyRequest(slot, request);

            // Chỉ kiểm khi slot chiếm giờ VÀ vừa đổi giờ hoặc vừa trở thành slot chiếm giờ (ví dụ Proposed sang Approved).
            // Sửa phần khác (ghi chú...) của slot cũ đã lỡ chồng giờ thì không bị chặn
            if ((slot.StartTime != oldStart || slot.EndTime != oldEnd || !wasLive))
            {
                await EnsureNoOverlapAsync(userId, slot, cancellationToken);
            }

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

            var result = new ProgressResult { TaskId = slot.TaskId };
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Slot đổi trạng thái có thể làm Task đổi giữa ToDo, Scheduled và InProgress
                result.Merge(await _progress.RefreshTaskAsync(slot.TaskId!.Value, cancellationToken));
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }, cancellationToken);

            SyncJobs(slot);
            await _progress.AfterCommitAsync(result, cancellationToken);
            return ToDto(slot);
        }

        public async Task<ScheduleSlotDto> CancelSlotAsync(Guid userId, Guid slotId, CancellationToken cancellationToken = default)
        {
            var slot = await GetOwnedSlotAsync(userId, slotId, cancellationToken);

            if (slot.GroupTaskId is not null)
            {
                return await _groupSchedule.CancelSlotAsync(userId, slot, cancellationToken);
            }

            if (slot.Status == CancelledStatus)
            {
                throw new UserFriendlyException("Slot này đã bị hủy trước đó.");
            }

            // Slot đã Done hoặc Skipped là trạng thái chốt, không hủy được
            StatusRules.EnsureSlot(slot.Status, CancelledStatus);

            var oldStart = slot.StartTime;
            var oldEnd = slot.EndTime;
            slot.Status = CancelledStatus;

            var result = new ProgressResult { TaskId = slot.TaskId };
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await AddLogAsync(slot.Id, "Deleted", oldStart, oldEnd, null, null, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Hủy hết slot sống thì Task không còn Scheduled
                result.Merge(await _progress.RefreshTaskAsync(slot.TaskId!.Value, cancellationToken));
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }, cancellationToken);

            _jobService.CancelAllJobs(slot.Id);
            await _progress.AfterCommitAsync(result, cancellationToken);

            return ToDto(slot);
        }

        // Giữ job nhắc việc khớp với slot sau mỗi lần tạo hoặc sửa. Đã lưu xong mới gọi: lỗi hẹn giờ không làm mất thay đổi.
        //  - Approved: hẹn lại toàn bộ theo StartTime và EndTime hiện tại (job cũ bị hủy, tránh nổ sai giờ)
        //  - Due, InProgress, Overrun: đang diễn ra, để nguyên job
        //  - Còn lại (Proposed, Cancelled, Done, Skipped, Missed, Postponed): không cần nhắc, hủy hết
        private void SyncJobs(ScheduleSlot slot)
        {
            switch (slot.Status)
            {
                case "Approved":
                    _jobService.ScheduleAllJobsForSlot(slot);
                    break;
                case "Due":
                case "InProgress":
                case "Overrun":
                    break;
                default:
                    _jobService.CancelAllJobs(slot.Id);
                    break;
            }
        }

        // Slot đang chiếm giờ (Approved, Due, InProgress, Overrun) không được chồng lên slot chiếm giờ khác của cùng người dùng
        // (giao nhau mới tính, chạm đầu mút như 7h-8h và 8h-9h thì được). Slot không chiếm giờ thì không kiểm
        private async Task EnsureNoOverlapAsync(Guid userId, ScheduleSlot slot, CancellationToken cancellationToken)
        {
            if (!StatusRules.IsLiveSlot(slot.Status))
            {
                return;
            }

            var overlaps = await _slotRepository.GetLiveSlotsOverlappingAsync(userId, slot.StartTime, slot.EndTime, slot.Id, cancellationToken);
            if (overlaps.Count > 0)
            {
                throw SlotOverlap.Create(overlaps);
            }
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
                throw new UserFriendlyException("Trạng thái không hợp lệ. Chọn một trong: Proposed, Approved, Due, InProgress, Done, Skipped, Missed, Overrun, Postponed, Cancelled.");
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
            return SlotMapper.ToDto(slot);
        }
    }
}
