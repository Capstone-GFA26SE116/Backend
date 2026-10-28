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
    ILogger<SlotBackgroundJobs> logger)
{
    // Còn 15 phút nữa đến giờ: chỉ nhắc khi slot đã được duyệt và chưa tới giờ
    public async Task PreStartReminder(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !Is(slot, ScheduleSlotStatus.Approved)) return;

        await Notify(slot, "Sắp đến giờ làm việc",
            $"Công việc \"{slot.Task.Title}\" sẽ bắt đầu trong 15 phút nữa.", "PRE_START_REMINDER");
    }

    // Đến giờ: KHÔNG tự bấm giờ giúp người dùng, chỉ đổi trạng thái và hiện nút Start
    public async Task SlotDue(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !Is(slot, ScheduleSlotStatus.Approved)) return;

        slot.Status = ScheduleSlotStatus.Due.ToString();
        await dbContext.SaveChangesAsync();

        await Notify(slot, "Đến giờ làm việc",
            $"Đến giờ rồi! Bấm Start để bắt đầu \"{slot.Task.Title}\".", "START_PROMPT");
    }

    // 20 phút sau giờ bắt đầu mà vẫn chưa ai bấm Start (và chưa làm phiên nào trước đó)
    public async Task NotStartedNudge(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !Is(slot, ScheduleSlotStatus.Due)) return;
        if (await dbContext.FocusSessions.AnyAsync(s => s.ScheduleSlotId == slotId)) return;

        await Notify(slot, "Bạn chưa bắt đầu",
            $"Bạn vẫn chưa bắt đầu \"{slot.Task.Title}\". Bấm Start ngay nhé!", "NOT_STARTED");
    }

    // Còn 10 phút là hết giờ dự kiến: chỉ nhắc khi đang có phiên chạy (slot ở trạng thái InProgress)
    public async Task NearEndReminder(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !Is(slot, ScheduleSlotStatus.InProgress)) return;

        await Notify(slot, "Sắp hết giờ",
            $"\"{slot.Task.Title}\" còn khoảng 10 phút nữa là hết giờ dự kiến.", "NEAR_END");
    }

    // Hết giờ dự kiến
    public async Task SlotEnded(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null) return;

        if (Is(slot, ScheduleSlotStatus.InProgress))
        {
            // Vẫn đang làm: chuyển sang Overrun, đợi người dùng bấm Stop (hoặc AutoTimeout cắt)
            slot.Status = ScheduleSlotStatus.Overrun.ToString();
            await dbContext.SaveChangesAsync();
            return;
        }

        if (!Is(slot, ScheduleSlotStatus.Due) && !Is(slot, ScheduleSlotStatus.Approved)) return;

        if (await dbContext.FocusSessions.AnyAsync(s => s.ScheduleSlotId == slotId))
        {
            // Đã làm một phần rồi tạm dừng nhưng chưa ghi nhận xong: hỏi lại người dùng, không coi là Missed
            await Notify(slot, "Công việc đã hết giờ",
                $"Bạn đã tạm dừng \"{slot.Task.Title}\". Công việc đã xong chưa?", "CHECK_IN");
            return;
        }

        // Chưa từng bấm Start
        slot.Status = ScheduleSlotStatus.Missed.ToString();
        await dbContext.SaveChangesAsync();

        await Notify(slot, "Bạn đã bỏ lỡ công việc",
            $"\"{slot.Task.Title}\" đã hết giờ mà bạn chưa bắt đầu.", NotificationService.MissedSlotAction);
    }

    // 15 phút sau khi hết giờ mà phiên vẫn chạy
    public async Task OverrunReminder(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !Is(slot, ScheduleSlotStatus.Overrun)) return;
        if (!await dbContext.FocusSessions.AnyAsync(s => s.ScheduleSlotId == slotId && s.EndedAt == null)) return;

        await Notify(slot, "Đã quá giờ dự kiến",
            $"Bạn còn đang làm \"{slot.Task.Title}\"? Nhớ bấm Stop khi xong nhé.", "OVERRUN");
    }

    // Chưa làm: hệ thống chưa lưu câu trả lời check-in ở đâu nên không biết người dùng đã trả lời hay chưa,
    // gửi lại sẽ làm phiền người đã trả lời. Giữ chỗ để bổ sung khi có nơi lưu check-in.
    public Task CheckInRetry(Guid slotId) => Task.CompletedTask;

    // Phiên chạy quá 2 lần thời gian dự kiến: người dùng gần như chắc chắn đã quên Stop
    public async Task AutoTimeout(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || (!Is(slot, ScheduleSlotStatus.InProgress) && !Is(slot, ScheduleSlotStatus.Overrun))) return;

        var session = await dbContext.FocusSessions
            .FirstOrDefaultAsync(s => s.ScheduleSlotId == slotId && s.EndedAt == null);
        if (session is null) return;

        session.EndedAt = timeProvider.GetUtcNow().UtcDateTime;
        session.CaptureMethod = CaptureMethod.AutoTimeout.ToString();
        session.StopReason = FocusStopReason.Other.ToString();
        // Chưa được xác nhận thì không đưa vào việc học (xem POST /api/FocusSession/{id}/confirm)
        session.IsConfirmed = false;
        session.Note = "Hệ thống tự động ngắt do vượt quá 2 lần thời gian dự kiến.";
        slot.Status = ScheduleSlotStatus.Done.ToString();
        await dbContext.SaveChangesAsync();

        await Notify(slot, "Phiên làm việc bị ngắt",
            $"Phiên \"{slot.Task.Title}\" đã bị ngắt tự động vì chạy quá lâu. Hãy xác nhận thời gian làm thực tế.", "AUTO_TIMEOUT");
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

    private Task<ScheduleSlot?> GetSlotAsync(Guid slotId)
    {
        return dbContext.ScheduleSlots
            .Include(s => s.Task)
            .ThenInclude(t => t.Project)
            .FirstOrDefaultAsync(s => s.Id == slotId);
    }

    private Task Notify(ScheduleSlot slot, string title, string message, string type)
    {
        return notification.SendAsync(slot.Task.Project.UserId, title, message, type, slot.Id);
    }

    private static bool Is(ScheduleSlot slot, ScheduleSlotStatus expected)
    {
        return string.Equals(slot.Status, expected.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
