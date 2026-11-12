using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using DomainTask = METANOIA.Domain.Entities.Task;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class TaskProgressService : ITaskProgressService
    {
        private const string ManualSource = "Manual";

        private readonly AppDbContext _context;
        private readonly IScheduleJobService _jobService;
        private readonly IAiServiceClient _aiServiceClient;

        public TaskProgressService(AppDbContext context, IScheduleJobService jobService, IAiServiceClient aiServiceClient)
        {
            _context = context;
            _jobService = jobService;
            _aiServiceClient = aiServiceClient;
        }

        public async Task<ProgressResult> RefreshTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            var result = new ProgressResult { TaskId = taskId };

            var task = await LoadAsync(taskId, cancellationToken);
            if (task is null)
            {
                return result;
            }

            // Skipped là trạng thái chốt do người dùng đặt: không tự đổi
            if (task.Status != StatusRules.Skipped)
            {
                var subTasks = task.SubTasks.ToList();
                var hasRunningSession = task.FocusSessions.Any(s => s.EndedAt is null);

                if (task.Status == StatusRules.Done)
                {
                    // Subtask mới hoặc subtask được mở lại (ToDo) thì Task đã Done phải mở lại
                    if (subTasks.Any(s => s.Status == StatusRules.ToDo))
                    {
                        task.CompletedAt = null;
                        task.Status = DeriveLiveStatus(task, subTasks);
                    }
                }
                else
                {
                    // Mọi subtask đã xử lý xong (Done hoặc Skipped, ít nhất một Done): Task tự Done.
                    // Đang có phiên chạy thì chưa đóng (người dùng còn đang làm)
                    var allHandled = subTasks.Count > 0
                        && subTasks.All(s => s.Status != StatusRules.ToDo)
                        && subTasks.Any(s => s.Status == StatusRules.Done);

                    if (allHandled && !hasRunningSession)
                    {
                        task.Status = StatusRules.Done;
                        task.CompletedAt ??= DateTime.UtcNow;
                        CancelNotStartedSlots(task, result);
                        result.TaskBecameDone = true;
                    }
                    else
                    {
                        task.Status = DeriveLiveStatus(task, subTasks);
                    }
                }
            }

            // Trạng thái mới của task mới chỉ nằm trong bộ nhớ (chưa SaveChanges), nên báo thẳng cho giai đoạn
            await RefreshMilestoneCoreAsync(
                task.MilestoneId,
                task.Status is StatusRules.InProgress or StatusRules.Done,
                cancellationToken);
            return result;
        }

        public async Task CascadeCloseAsync(Guid taskId, ProgressResult result, CancellationToken cancellationToken = default)
        {
            var task = await LoadAsync(taskId, cancellationToken);
            if (task is null)
            {
                return;
            }

            foreach (var subTask in task.SubTasks.Where(s => s.Status == StatusRules.ToDo))
            {
                subTask.Status = StatusRules.Skipped;
            }

            CancelNotStartedSlots(task, result);
        }

        // Giai đoạn Planned tự lên InProgress khi có task bắt đầu làm hoặc đã xong. Không tự đổi các bước sau
        // (Delivered, InRevision, Approved là việc giao nhận với khách do người dùng quyết định)
        public Task RefreshMilestoneAsync(Guid? milestoneId, CancellationToken cancellationToken = default)
        {
            return RefreshMilestoneCoreAsync(milestoneId, taskAlreadyStarted: false, cancellationToken);
        }

        // taskAlreadyStarted: người gọi vừa đặt một task của giai đoạn sang InProgress hoặc Done mà chưa lưu
        private async Task RefreshMilestoneCoreAsync(Guid? milestoneId, bool taskAlreadyStarted, CancellationToken cancellationToken)
        {
            if (milestoneId is not { } id)
            {
                return;
            }

            var milestone = await _context.Milestones.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
            if (milestone is null || milestone.Status != StatusRules.Planned)
            {
                return;
            }

            var started = taskAlreadyStarted || await _context.Tasks.AnyAsync(
                t => t.MilestoneId == id && (t.Status == StatusRules.InProgress || t.Status == StatusRules.Done),
                cancellationToken);
            if (started)
            {
                milestone.Status = StatusRules.InProgress;
            }
        }

        public async Task AfterCommitAsync(ProgressResult result, CancellationToken cancellationToken = default)
        {
            foreach (var slotId in result.CancelledSlotIds.Distinct())
            {
                _jobService.CancelAllJobs(slotId);
            }

            if (result.TaskBecameDone && result.TaskId is { } taskId)
            {
                // Không chặn luồng chính nếu dịch vụ AI lỗi (đã được xử lý trong client)
                await _aiServiceClient.RecordTaskCompletionAsync(taskId, cancellationToken);
            }
        }

        private Task<DomainTask?> LoadAsync(Guid taskId, CancellationToken cancellationToken)
        {
            return _context.Tasks
                .Include(t => t.SubTasks)
                .Include(t => t.ScheduleSlots)
                .Include(t => t.FocusSessions)
                .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        }

        // Trạng thái "sống" suy ra từ sự thật:
        //  - InProgress: có phiên đang chạy, hoặc phiên đã kết thúc ĐƯỢC TÍNH (không phải AutoTimeout chưa xác nhận), hoặc có subtask Done
        //  - Scheduled : chưa làm gì nhưng có slot sống trên lịch
        //  - ToDo      : còn lại
        private static string DeriveLiveStatus(DomainTask task, IReadOnlyCollection<SubTask> subTasks)
        {
            var hasProgress = task.FocusSessions.Any(s => s.EndedAt is null || CountsAsProgress(s))
                || subTasks.Any(s => s.Status == StatusRules.Done);
            if (hasProgress)
            {
                return StatusRules.InProgress;
            }

            return task.ScheduleSlots.Any(s => StatusRules.IsLiveSlot(s.Status)) ? StatusRules.Scheduled : StatusRules.ToDo;
        }

        // Phiên AutoTimeout chưa xác nhận (hoặc đã bị loại bỏ) không tính là "đã làm"
        private static bool CountsAsProgress(FocusSession session)
        {
            return session.IsConfirmed || session.CaptureMethod != nameof(CaptureMethod.AutoTimeout);
        }

        private void CancelNotStartedSlots(DomainTask task, ProgressResult result)
        {
            var now = DateTime.UtcNow;
            foreach (var slot in task.ScheduleSlots.Where(s => StatusRules.IsNotStartedSlot(s.Status)).ToList())
            {
                var oldStart = slot.StartTime;
                var oldEnd = slot.EndTime;
                slot.Status = nameof(ScheduleSlotStatus.Cancelled);

                _context.ScheduleChangeLogs.Add(new ScheduleChangeLog
                {
                    Id = Guid.NewGuid(),
                    ScheduleSlotId = slot.Id,
                    ChangeType = "Deleted",
                    Source = ManualSource,
                    OldStart = oldStart,
                    OldEnd = oldEnd,
                    ChangedAt = now
                });

                result.CancelledSlotIds.Add(slot.Id);
            }
        }
    }
}
