using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Member tự xếp giờ làm cho việc (hoặc việc con) được giao cho mình. Dùng chung bảng ScheduleSlot với nhánh cá nhân nên có sẵn nhắc việc,
    // đồng bộ Google Calendar, lịch gộp, lịch sử đổi giờ và luật "slot không chồng giờ" (docs/GROUP_FEATURE.md mục 13).
    //  - Slot nhóm thuộc người đã xếp (ScheduleSlot.UserId); Leader không xếp hộ.
    //  - Việc có việc con thì phải xếp cho việc con (giống luật bấm giờ).
    //  - Slot nhóm không làm đổi status của việc nhóm (việc nhóm chỉ có ToDo, InProgress, Done, Skipped).
    public class GroupScheduleService : IGroupScheduleService
    {
        private const string ProposedStatus = "Proposed";
        private const string ApprovedStatus = "Approved";
        private const string CancelledStatus = "Cancelled";
        private const string ManualSource = "Manual";
        private const int MaxExplanationLength = 500;

        private static readonly HashSet<string> AllowedStatuses = new()
        {
            ProposedStatus, ApprovedStatus, "Due", "InProgress", "Done", "Skipped", "Missed", "Overrun", "Postponed", CancelledStatus
        };

        private readonly IScheduleSlotRepository _slots;
        private readonly IScheduleChangeLogRepository _logs;
        private readonly IGroupWorkRepository _work;
        private readonly IGroupAccessService _access;
        private readonly IScheduleJobService _jobs;
        private readonly IUnitOfWork _unitOfWork;

        public GroupScheduleService(
            IScheduleSlotRepository slots,
            IScheduleChangeLogRepository logs,
            IGroupWorkRepository work,
            IGroupAccessService access,
            IScheduleJobService jobs,
            IUnitOfWork unitOfWork)
        {
            _slots = slots;
            _logs = logs;
            _work = work;
            _access = access;
            _jobs = jobs;
            _unitOfWork = unitOfWork;
        }

        public async Task<GroupSlotDto> CreateSlotAsync(Guid userId, Guid taskId, GroupSlotRequestDto request, CancellationToken cancellationToken = default)
        {
            var task = await GetTaskAsync(taskId, cancellationToken);
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireCanWork();
            access.RequireActive();

            if (GroupRules.IsClosed(task.Status))
            {
                throw new UserFriendlyException($"Việc đang ở trạng thái '{task.Status}'. Hãy mở lại việc trước khi xếp lịch.");
            }

            GroupSubTask? subTask = null;
            if (request.SubTaskId is { } subTaskId)
            {
                subTask = task.SubTasks.FirstOrDefault(s => s.Id == subTaskId)
                    ?? throw new UserFriendlyException("Việc con không thuộc việc này.");

                if (GroupRules.IsClosed(subTask.Status))
                {
                    throw new UserFriendlyException($"Việc con đang ở trạng thái '{subTask.Status}'. Hãy mở lại việc con trước khi xếp lịch.");
                }
            }
            else if (task.SubTasks.Count > 0)
            {
                throw new UserFriendlyException("Việc này có việc con. Hãy chọn việc con để xếp lịch.");
            }

            if (!GroupRules.IsAssignedTo(task, subTask, userId))
            {
                throw new GroupForbiddenException("Bạn chỉ xếp lịch được cho việc (hoặc việc con) được giao cho mình.");
            }

            ValidateTimes(request.StartTime, request.EndTime, request.Explanation);

            var status = string.IsNullOrWhiteSpace(request.Status) ? ApprovedStatus : request.Status;
            if (status is not (ApprovedStatus or ProposedStatus))
            {
                throw new UserFriendlyException("Slot mới chỉ tạo được ở trạng thái Approved hoặc Proposed.");
            }

            var slot = new ScheduleSlot
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                GroupTaskId = task.Id,
                GroupSubTaskId = subTask?.Id,
                StartTime = UtcTime.ToStorage(request.StartTime),
                EndTime = UtcTime.ToStorage(request.EndTime),
                ProposedMinutes = Minutes(request.StartTime, request.EndTime),
                IsManualOverride = true,
                Explanation = request.Explanation,
                Status = status
            };

            await EnsureNoOverlapAsync(userId, slot, cancellationToken);

            await _slots.AddAsync(slot, cancellationToken);
            await AddLogAsync(slot.Id, "Created", null, null, slot.StartTime, slot.EndTime, cancellationToken);
            await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId,
                subTask is null ? GroupEntityTypes.Task : GroupEntityTypes.SubTask, subTask?.Id ?? task.Id,
                "SlotCreated", null, $"{slot.StartTime:yyyy-MM-dd HH:mm} UTC, {slot.ProposedMinutes} phút"), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            SyncJobs(slot);

            var users = await _work.GetUserNamesAsync([userId], cancellationToken);
            return ToGroupDto(slot, task, subTask, users.GetValueOrDefault(userId, string.Empty));
        }

        public async Task<IReadOnlyList<GroupSlotDto>> GetSlotsAsync(Guid userId, Guid taskId, bool includeCancelled, CancellationToken cancellationToken = default)
        {
            var task = await GetTaskAsync(taskId, cancellationToken);
            await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);

            var rows = await _slots.GetByGroupTaskIdAsync(taskId, includeCancelled, cancellationToken);
            return rows.Select(s => ToGroupDto(s, s.GroupTask!, s.GroupSubTask, s.User?.FullName ?? string.Empty)).ToList();
        }

        public async Task<ScheduleSlotDto> UpdateSlotAsync(Guid userId, ScheduleSlot slot, ScheduleSlotRequestDto request, CancellationToken cancellationToken = default)
        {
            var (task, subTask) = await LoadItemAsync(slot, cancellationToken);
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireCanWork();
            access.RequireActive();

            if (!GroupRules.IsAssignedTo(task, subTask, userId))
            {
                throw new UserFriendlyException("Việc này không còn giao cho bạn nên không sửa được slot.");
            }

            ValidateTimes(request.StartTime, request.EndTime, request.Explanation);
            if (request.Status is not null && !AllowedStatuses.Contains(request.Status))
            {
                throw new UserFriendlyException("Trạng thái không hợp lệ. Chọn một trong: Proposed, Approved, Due, InProgress, Done, Skipped, Missed, Overrun, Postponed, Cancelled.");
            }

            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                StatusRules.EnsureSlot(slot.Status, request.Status);
            }

            var oldStart = slot.StartTime;
            var oldEnd = slot.EndTime;
            var wasLive = StatusRules.IsLiveSlot(slot.Status);

            slot.StartTime = UtcTime.ToStorage(request.StartTime);
            slot.EndTime = UtcTime.ToStorage(request.EndTime);
            slot.ProposedMinutes = Minutes(request.StartTime, request.EndTime);
            slot.OriginalStartTime = request.OriginalStartTime is { } original ? UtcTime.ToStorage(original) : slot.OriginalStartTime;
            slot.IsManualOverride = true;
            slot.Explanation = request.Explanation;
            slot.Status = string.IsNullOrWhiteSpace(request.Status) ? slot.Status : request.Status;

            var startChanged = slot.StartTime != oldStart;
            var endChanged = slot.EndTime != oldEnd;

            // Chỉ kiểm khi slot chiếm giờ VÀ vừa đổi giờ hoặc vừa chuyển sang trạng thái chiếm giờ
            if (startChanged || endChanged || !wasLive)
            {
                await EnsureNoOverlapAsync(userId, slot, cancellationToken);
            }

            if (startChanged || endChanged)
            {
                if (startChanged && slot.OriginalStartTime is null)
                {
                    slot.OriginalStartTime = oldStart;
                }

                await AddLogAsync(slot.Id, startChanged ? "Moved" : "Resized", oldStart, oldEnd, slot.StartTime, slot.EndTime, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            SyncJobs(slot);
            return SlotMapper.ToDto(slot);
        }

        public async Task<ScheduleSlotDto> CancelSlotAsync(Guid userId, ScheduleSlot slot, CancellationToken cancellationToken = default)
        {
            var (task, _) = await LoadItemAsync(slot, cancellationToken);
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireCanWork();

            if (slot.Status == CancelledStatus)
            {
                throw new UserFriendlyException("Slot này đã bị hủy trước đó.");
            }

            // Slot đã Done hoặc Skipped là trạng thái chốt, không hủy được
            StatusRules.EnsureSlot(slot.Status, CancelledStatus);

            var oldStart = slot.StartTime;
            var oldEnd = slot.EndTime;
            slot.Status = CancelledStatus;
            await AddLogAsync(slot.Id, "Deleted", oldStart, oldEnd, null, null, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _jobs.CancelAllJobs(slot.Id);
            return SlotMapper.ToDto(slot);
        }

        // ---------------------------------------------------------------- Dùng chung

        private async Task<GroupTask> GetTaskAsync(Guid taskId, CancellationToken cancellationToken)
        {
            return await _work.GetTaskAsync(taskId, cancellationToken) ?? throw new NotFoundException("Không tìm thấy việc.");
        }

        private async Task<(GroupTask Task, GroupSubTask? SubTask)> LoadItemAsync(ScheduleSlot slot, CancellationToken cancellationToken)
        {
            var task = await GetTaskAsync(slot.GroupTaskId!.Value, cancellationToken);
            var subTask = slot.GroupSubTaskId is { } id ? task.SubTasks.FirstOrDefault(s => s.Id == id) : null;
            return (task, subTask);
        }

        // Slot chiếm giờ (Approved, Due, InProgress, Overrun) không được chồng lên slot chiếm giờ khác của cùng người, kể cả slot cá nhân
        private async Task EnsureNoOverlapAsync(Guid userId, ScheduleSlot slot, CancellationToken cancellationToken)
        {
            if (!StatusRules.IsLiveSlot(slot.Status))
            {
                return;
            }

            var overlaps = await _slots.GetLiveSlotsOverlappingAsync(userId, slot.StartTime, slot.EndTime, slot.Id, cancellationToken);
            if (overlaps.Count > 0)
            {
                throw SlotOverlap.Create(overlaps);
            }
        }

        private static void ValidateTimes(DateTimeOffset start, DateTimeOffset end, string? explanation)
        {
            if (end <= start)
            {
                throw new UserFriendlyException("EndTime phải lớn hơn StartTime.");
            }

            if (explanation?.Length > MaxExplanationLength)
            {
                throw new UserFriendlyException($"Giải thích tối đa {MaxExplanationLength} ký tự.");
            }
        }

        private static int Minutes(DateTimeOffset start, DateTimeOffset end) => (int)Math.Round((end - start).TotalMinutes);

        // Giữ job nhắc việc khớp với slot sau mỗi lần tạo hoặc sửa (giống ScheduleSlotService): Approved thì hẹn lại toàn bộ,
        // đang diễn ra thì để nguyên, còn lại thì hủy hết
        private void SyncJobs(ScheduleSlot slot)
        {
            switch (slot.Status)
            {
                case ApprovedStatus:
                    _jobs.ScheduleAllJobsForSlot(slot);
                    break;
                case "Due":
                case "InProgress":
                case "Overrun":
                    break;
                default:
                    _jobs.CancelAllJobs(slot.Id);
                    break;
            }
        }

        private Task AddLogAsync(Guid slotId, string changeType, DateTime? oldStart, DateTime? oldEnd, DateTime? newStart, DateTime? newEnd, CancellationToken cancellationToken)
        {
            return _logs.AddAsync(new ScheduleChangeLog
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

        private static GroupSlotDto ToGroupDto(ScheduleSlot slot, GroupTask task, GroupSubTask? subTask, string userName)
        {
            var dto = SlotMapper.Map<GroupSlotDto>(slot);
            dto.GroupId = task.GroupId;
            dto.UserName = userName;
            dto.TaskTitle = task.Title;
            dto.SubTaskTitle = subTask?.Title;
            return dto;
        }
    }
}
