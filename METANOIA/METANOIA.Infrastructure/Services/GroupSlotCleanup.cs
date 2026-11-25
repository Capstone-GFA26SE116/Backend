using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Dọn slot nhóm khi việc đổi người hoặc thành viên rời nhóm (docs/GROUP_FEATURE.md mục 13): slot thuộc người đã xếp, người mới
    // không thừa hưởng, nên các slot CHƯA BẮT ĐẦU của người cũ bị hủy (không xóa dòng, còn lịch sử); phiên bấm giờ đang chạy của người
    // cũ trên việc đó được dừng. Slot đã xong, bỏ lỡ, hoãn hoặc bỏ qua giữ nguyên làm lịch sử.
    // Dùng hai bước: Stage* chỉ ghi thay đổi vào context (người gọi tự lưu, cùng transaction với việc giao lại), rồi FinishAsync
    // (sau khi đã lưu) hủy job nhắc và báo cho người cũ.
    public sealed class GroupSlotCleanup
    {
        private const int MaxSessionHours = 12;
        private const string ManualSource = "Manual";

        private readonly IScheduleSlotRepository _slots;
        private readonly IScheduleChangeLogRepository _logs;
        private readonly IGroupWorkRepository _work;
        private readonly IScheduleJobService _jobs;
        private readonly IGroupNotifier _notifier;
        private readonly TimeProvider _timeProvider;

        public GroupSlotCleanup(
            IScheduleSlotRepository slots,
            IScheduleChangeLogRepository logs,
            IGroupWorkRepository work,
            IScheduleJobService jobs,
            IGroupNotifier notifier,
            TimeProvider timeProvider)
        {
            _slots = slots;
            _logs = logs;
            _work = work;
            _jobs = jobs;
            _notifier = notifier;
            _timeProvider = timeProvider;
        }

        public sealed record CancelledSlot(Guid SlotId, Guid UserId, Guid GroupId, Guid GroupTaskId, string TaskTitle, string GroupName);

        public sealed class Result
        {
            public List<CancelledSlot> Cancelled { get; } = new();

            public int StoppedSessions { get; set; }
        }

        // Việc (hoặc việc con) vừa đổi người: hủy slot chưa bắt đầu của những người không còn là người được giao hiệu lực, dừng phiên của họ.
        // task phải kèm việc con (GroupWorkRepository.GetTaskAsync)
        public async Task<Result> StageOrphanedOfTaskAsync(GroupTask task, CancellationToken cancellationToken)
        {
            var result = new Result();
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            foreach (var session in await _work.GetRunningFocusSessionsOfTaskAsync(task.Id, cancellationToken))
            {
                var subTask = session.GroupSubTaskId is { } id ? task.SubTasks.FirstOrDefault(s => s.Id == id) : null;
                if (!GroupRules.IsAssignedTo(task, subTask, session.UserId))
                {
                    StopSession(session, now, "Việc đã được giao cho người khác.");
                    result.StoppedSessions++;
                }
            }

            foreach (var slot in await _slots.GetOpenSlotsOfGroupTaskAsync(task.Id, cancellationToken))
            {
                var subTask = slot.GroupSubTaskId is { } id ? task.SubTasks.FirstOrDefault(s => s.Id == id) : null;
                if (slot.UserId is { } owner && !GroupRules.IsAssignedTo(task, subTask, owner))
                {
                    await CancelAsync(slot, result, now, cancellationToken);
                }
            }

            return result;
        }

        // Người dùng rời nhóm, bị xóa khỏi nhóm hoặc hạ xuống Viewer: hủy mọi slot chưa bắt đầu của họ trong nhóm (kể cả việc giao cho người khác)
        // và dừng phiên đang chạy trong nhóm này
        public async Task<Result> StageAllOfUserInGroupAsync(Guid groupId, Guid userId, CancellationToken cancellationToken)
        {
            var result = new Result();
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            var running = await _work.GetRunningFocusSessionAsync(userId, cancellationToken);
            if (running is not null && running.GroupTask.GroupId == groupId && running.EndedAt is null)
            {
                StopSession(running, now, "Người làm không còn trong nhóm.");
                result.StoppedSessions++;
            }

            foreach (var slot in await _slots.GetOpenSlotsOfUserInGroupAsync(userId, groupId, cancellationToken))
            {
                await CancelAsync(slot, result, now, cancellationToken);
            }

            return result;
        }

        // Xóa việc nhóm: xóa luôn mọi slot của việc cùng lịch sử đổi giờ (phiên bấm giờ giữ lại, ScheduleSlotId về rỗng)
        public async Task<List<Guid>> StageDeleteSlotsOfTaskAsync(Guid groupTaskId, CancellationToken cancellationToken)
        {
            var slots = await _slots.GetSlotsWithLogsOfGroupTaskAsync(groupTaskId, cancellationToken);
            foreach (var slot in slots)
            {
                _slots.Remove(slot);
            }

            return slots.Select(s => s.Id).ToList();
        }

        public Task FinishDeletedAsync(IEnumerable<Guid> slotIds)
        {
            foreach (var slotId in slotIds)
            {
                _jobs.CancelAllJobs(slotId);
            }

            return Task.CompletedTask;
        }

        // Sau khi đã lưu: hủy job nhắc (lần đồng bộ Google sau sẽ xóa sự kiện của slot đã hủy) và báo cho từng người bị hủy lịch
        public async Task FinishAsync(Result result, string reasonText, CancellationToken cancellationToken)
        {
            foreach (var slot in result.Cancelled)
            {
                _jobs.CancelAllJobs(slot.SlotId);
            }

            foreach (var group in result.Cancelled.GroupBy(c => (c.UserId, c.GroupTaskId)))
            {
                var first = group.First();
                await _notifier.NotifyAsync(first.UserId, GroupNotificationTypes.StatusChanged, "Lịch làm việc nhóm đã bị hủy",
                    $"{reasonText} Việc \"{first.TaskTitle}\" của nhóm \"{first.GroupName}\": {group.Count()} slot chưa bắt đầu của bạn đã bị hủy.",
                    first.GroupId, first.GroupTaskId);
            }
        }

        private async Task CancelAsync(ScheduleSlot slot, Result result, DateTime now, CancellationToken cancellationToken)
        {
            var oldStart = slot.StartTime;
            var oldEnd = slot.EndTime;

            // Slot đang làm không hủy thẳng được: về Due (phiên đã được dừng ở trên) rồi mới Cancelled
            if (slot.Status is "InProgress" or "Overrun")
            {
                StatusRules.EnsureSlot(slot.Status, "Due");
                slot.Status = "Due";
            }

            StatusRules.EnsureSlot(slot.Status, "Cancelled");
            slot.Status = "Cancelled";

            await _logs.AddAsync(new ScheduleChangeLog
            {
                Id = Guid.NewGuid(),
                ScheduleSlotId = slot.Id,
                ChangeType = "Deleted",
                Source = ManualSource,
                OldStart = oldStart,
                OldEnd = oldEnd,
                ChangedAt = now
            }, cancellationToken);

            result.Cancelled.Add(new CancelledSlot(
                slot.Id, slot.UserId!.Value, slot.GroupTask!.GroupId, slot.GroupTaskId!.Value, slot.GroupTask.Title, slot.GroupTask.Group.Name));
        }

        private static void StopSession(GroupFocusSession session, DateTime now, string note)
        {
            var maxEnd = session.StartedAt.AddHours(MaxSessionHours);
            session.EndedAt = now > maxEnd ? maxEnd : now;
            session.StopReason = FocusStopReason.Other.ToString();
            session.Note = note;
        }
    }
}
