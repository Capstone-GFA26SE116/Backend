using Hangfire;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Enums;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScheduleSlot = METANOIA.Domain.Entities.ScheduleSlot;

namespace METANOIA.Infrastructure.Services;

// Các việc Hangfire chạy nền cho từng slot (xem ScheduleJobService để biết giờ chạy).
// Mỗi job TỰ KIỂM TRA slot còn đúng trạng thái mong đợi rồi mới làm: slot có thể đã bị dời, hủy hoặc làm xong từ lúc job được hẹn.
// Retry ít lần để một lỗi tạm thời không gửi thông báo trùng nhiều lần.
[AutomaticRetry(Attempts = 2)]
public sealed class SlotBackgroundJobs(
    AppDbContext dbContext,
    INotificationService notification,
    TimeProvider timeProvider,
    ITaskProgressService progress,
    ILogger<SlotBackgroundJobs> logger)
{
    // Trước giờ bắt đầu theo số phút nhắc trước của người dùng (mặc định 15): chỉ nhắc khi slot đã được duyệt và chưa tới giờ
    public async Task PreStartReminder(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !Is(slot, ScheduleSlotStatus.Approved)) return;

        var start = DateTime.SpecifyKind(slot.StartTime, DateTimeKind.Utc);
        var minutes = Math.Max(1, (int)Math.Round((start - timeProvider.GetUtcNow().UtcDateTime).TotalMinutes));
        await Notify(slot, "Sắp đến giờ làm việc",
            $"Công việc \"{SlotOverlap.TitleOf(slot)}\" sẽ bắt đầu trong {minutes} phút nữa.", "PRE_START_REMINDER");
    }

    // Đến giờ: KHÔNG tự bấm giờ giúp người dùng, chỉ đổi trạng thái và hiện nút Start
    public async Task SlotDue(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !Is(slot, ScheduleSlotStatus.Approved)) return;

        StatusRules.EnsureSlot(slot.Status, nameof(ScheduleSlotStatus.Due));
        slot.Status = ScheduleSlotStatus.Due.ToString();
        await dbContext.SaveChangesAsync();

        await Notify(slot, "Đến giờ làm việc",
            $"Đến giờ rồi! Bấm Start để bắt đầu \"{SlotOverlap.TitleOf(slot)}\".", "START_PROMPT");
    }

    // 20 phút sau giờ bắt đầu mà vẫn chưa ai bấm Start (và chưa làm phiên nào trước đó)
    public async Task NotStartedNudge(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !Is(slot, ScheduleSlotStatus.Due)) return;
        if (await HasAnySessionAsync(slotId)) return;

        await Notify(slot, "Bạn chưa bắt đầu",
            $"Bạn vẫn chưa bắt đầu \"{SlotOverlap.TitleOf(slot)}\". Bấm Start ngay nhé!", "NOT_STARTED");
    }

    // Còn 10 phút là hết giờ dự kiến: chỉ nhắc khi đang có phiên chạy (slot ở trạng thái InProgress)
    public async Task NearEndReminder(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !Is(slot, ScheduleSlotStatus.InProgress)) return;

        await Notify(slot, "Sắp hết giờ",
            $"\"{SlotOverlap.TitleOf(slot)}\" còn khoảng 10 phút nữa là hết giờ dự kiến.", "NEAR_END");
    }

    // Hết giờ dự kiến
    public async Task SlotEnded(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null) return;

        if (Is(slot, ScheduleSlotStatus.InProgress))
        {
            // Vẫn đang làm: chuyển sang Overrun, đợi người dùng bấm Stop (hoặc AutoTimeout cắt)
            StatusRules.EnsureSlot(slot.Status, nameof(ScheduleSlotStatus.Overrun));
            slot.Status = ScheduleSlotStatus.Overrun.ToString();
            await dbContext.SaveChangesAsync();
            return;
        }

        if (!Is(slot, ScheduleSlotStatus.Due) && !Is(slot, ScheduleSlotStatus.Approved)) return;

        if (await HasAnySessionAsync(slotId))
        {
            // Đã làm một phần rồi tạm dừng nhưng chưa ghi nhận xong: hỏi lại người dùng, không coi là Missed
            await Notify(slot, "Công việc đã hết giờ",
                $"Bạn đã tạm dừng \"{SlotOverlap.TitleOf(slot)}\". Công việc đã xong chưa?", "CHECK_IN");
            return;
        }

        // Chưa từng bấm Start
        StatusRules.EnsureSlot(slot.Status, nameof(ScheduleSlotStatus.Missed));
        slot.Status = ScheduleSlotStatus.Missed.ToString();
        await dbContext.SaveChangesAsync();

        // Slot Missed không còn là slot sống: Task có thể từ Scheduled về ToDo (việc nhóm không có trạng thái Scheduled nên không cần)
        if (slot.TaskId is { } missedTaskId)
        {
            await progress.RefreshTaskAsync(missedTaskId);
            await dbContext.SaveChangesAsync();
        }

        await Notify(slot, "Bạn đã bỏ lỡ công việc",
            $"\"{SlotOverlap.TitleOf(slot)}\" đã hết giờ mà bạn chưa bắt đầu.", NotificationService.MissedSlotAction);
    }

    // 15 phút sau khi hết giờ mà phiên vẫn chạy
    public async Task OverrunReminder(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !Is(slot, ScheduleSlotStatus.Overrun)) return;
        if (!await dbContext.FocusSessions.AnyAsync(s => s.ScheduleSlotId == slotId && s.EndedAt == null)
            && !await dbContext.GroupFocusSessions.AnyAsync(s => s.ScheduleSlotId == slotId && s.EndedAt == null)) return;

        await Notify(slot, "Đã quá giờ dự kiến",
            $"Bạn còn đang làm \"{SlotOverlap.TitleOf(slot)}\"? Nhớ bấm Stop khi xong nhé.", "OVERRUN");
    }

    // Chưa làm: hệ thống chưa lưu câu trả lời check-in ở đâu nên không biết người dùng đã trả lời hay chưa,
    // gửi lại sẽ làm phiền người đã trả lời. Giữ chỗ để bổ sung khi có nơi lưu check-in.
    public Task CheckInRetry(Guid slotId) => Task.CompletedTask;

    // Phiên chạy quá 2 lần thời gian dự kiến: người dùng gần như chắc chắn đã quên Stop
    public async Task AutoTimeout(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || (!Is(slot, ScheduleSlotStatus.InProgress) && !Is(slot, ScheduleSlotStatus.Overrun))) return;

        // Phiên bấm giờ của nhóm không có AutoTimeout (tự cắt ở 12 giờ khi dừng)
        if (slot.GroupTaskId is not null) return;

        var session = await dbContext.FocusSessions
            .FirstOrDefaultAsync(s => s.ScheduleSlotId == slotId && s.EndedAt == null);
        if (session is null) return;

        session.EndedAt = timeProvider.GetUtcNow().UtcDateTime;
        session.CaptureMethod = CaptureMethod.AutoTimeout.ToString();
        session.StopReason = FocusStopReason.Other.ToString();
        // Chưa được xác nhận thì không đưa vào việc học (xem POST /api/FocusSession/{id}/confirm)
        session.IsConfirmed = false;
        session.Note = "Hệ thống tự động ngắt do vượt quá 2 lần thời gian dự kiến.";
        StatusRules.EnsureSlot(slot.Status, nameof(ScheduleSlotStatus.Done));
        slot.Status = ScheduleSlotStatus.Done.ToString();
        await dbContext.SaveChangesAsync();

        // Phiên chưa xác nhận không tính là "đã làm": Task suy lại (có thể về Scheduled hoặc ToDo cho tới khi người dùng xác nhận)
        await progress.RefreshTaskAsync(slot.TaskId!.Value);
        await dbContext.SaveChangesAsync();

        await Notify(slot, "Phiên làm việc bị ngắt",
            $"Phiên \"{SlotOverlap.TitleOf(slot)}\" đã bị ngắt tự động vì chạy quá lâu. Hãy xác nhận thời gian làm thực tế.", "AUTO_TIMEOUT");
    }

    // 24 giờ sau khi hết giờ. Phiên AutoTimeout chưa xác nhận đã tự bị loại khỏi việc học (IsConfirmed = false),
    // nên không cần ghi thêm gì. Trước đây job này ghi đè slot.Explanation, làm mất lời giải thích của AI.
    public async Task MarkUnknown(Guid slotId)
    {
        var unconfirmed = await dbContext.FocusSessions.CountAsync(s =>
            s.ScheduleSlotId == slotId && s.CaptureMethod == CaptureMethod.AutoTimeout.ToString() && !s.IsConfirmed);
        if (unconfirmed > 0)
        {
            logger.LogInformation("Slot {SlotId} còn {Count} phiên AutoTimeout chưa xác nhận: kết quả UNKNOWN, không dùng để học.", slotId, unconfirmed);
        }
    }

    // Slot nhóm chỉ còn "sống" khi nhóm chưa lưu trữ và việc (hoặc việc con) vẫn giao cho người đã xếp slot; không thì bỏ qua job
    private async Task<ScheduleSlot?> GetSlotAsync(Guid slotId)
    {
        var slot = await dbContext.ScheduleSlots
            .Include(s => s.Task)
            .ThenInclude(t => t!.Project)
            .Include(s => s.GroupTask).ThenInclude(t => t!.Group)
            .Include(s => s.GroupSubTask)
            .FirstOrDefaultAsync(s => s.Id == slotId);

        if (slot is { GroupTask: { } groupTask }
            && (groupTask.Group.Status != GroupStatuses.Active
                || (slot.GroupSubTask?.AssigneeId ?? groupTask.AssigneeId) != slot.UserId))
        {
            return null;
        }

        return slot;
    }

    // Slot đã có phiên làm việc nào chưa (phiên cá nhân hoặc phiên của nhóm)
    private async Task<bool> HasAnySessionAsync(Guid slotId)
    {
        return await dbContext.FocusSessions.AnyAsync(s => s.ScheduleSlotId == slotId)
            || await dbContext.GroupFocusSessions.AnyAsync(s => s.ScheduleSlotId == slotId);
    }

    private Task Notify(ScheduleSlot slot, string title, string message, string type)
    {
        // Slot nhóm: người nhận là người đã xếp slot (UserId); slot cá nhân: chủ dự án
        return notification.SendAsync(slot.UserId ?? slot.Task!.Project.UserId, title, message, type, slot.Id);
    }

    private static bool Is(ScheduleSlot slot, ScheduleSlotStatus expected)
    {
        return string.Equals(slot.Status, expected.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
