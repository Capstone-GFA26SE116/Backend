using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Enums;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using ScheduleSlot = METANOIA.Domain.Entities.ScheduleSlot;

namespace METANOIA.Infrastructure.Services;

public sealed class SlotBackgroundJobs(
    AppDbContext dbContext,
    INotificationService notification,
    TimeProvider timeProvider)
{
    public async Task PreStartReminder(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !HasStatus(slot.Status, ScheduleSlotStatus.Approved)) return;
        await notification.SendAsync(slot.Task.Project.UserId, "Focus session reminder", "Bạn có công việc chuẩn bị bắt đầu trong 15 phút nữa!", "PRE_START_REMINDER", slot.Id);
    }

    public async Task SlotDue(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !HasStatus(slot.Status, ScheduleSlotStatus.Approved)) return;
        slot.Status = ScheduleSlotStatus.Due.ToString();
        await dbContext.SaveChangesAsync();
        await notification.SendAsync(slot.Task.Project.UserId, "Focus session", "Đến giờ rồi! Hãy bấm Start để bắt đầu công việc.", "START_PROMPT", slot.Id);
    }

    public async Task NotStartedNudge(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !HasStatus(slot.Status, ScheduleSlotStatus.Due)) return;
        await notification.SendAsync(slot.Task.Project.UserId, "Focus session", "Bạn vẫn chưa bắt đầu công việc. Bấm Start ngay nhé!", "NOT_STARTED", slot.Id);
    }

    public async Task NearEndReminder(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !HasStatus(slot.Status, ScheduleSlotStatus.InProgress)) return;
        await notification.SendAsync(slot.Task.Project.UserId, "Focus session", "Công việc sắp kết thúc trong 10 phút.", "NEAR_END", slot.Id);
    }

    public async Task SlotEnded(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null) return;

        if (HasStatus(slot.Status, ScheduleSlotStatus.InProgress))
            slot.Status = ScheduleSlotStatus.Overrun.ToString();
        else if (HasStatus(slot.Status, ScheduleSlotStatus.Due) || HasStatus(slot.Status, ScheduleSlotStatus.Approved))
            slot.Status = ScheduleSlotStatus.Missed.ToString();
        else
            return;

        await dbContext.SaveChangesAsync();
    }

    public async Task OverrunReminder(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !HasStatus(slot.Status, ScheduleSlotStatus.Overrun)) return;

        var running = await dbContext.FocusSessions
            .AnyAsync(s => s.ScheduleSlotId == slotId && s.EndedAt == null);
        if (running)
            await notification.SendAsync(slot.Task.Project.UserId, "Focus session", "Đã quá giờ dự kiến. Nhớ bấm Stop khi xong nhé.", "OVERRUN", slot.Id);
    }

    public Task CheckInRetry(Guid slotId) => Task.CompletedTask;

    public async Task AutoTimeout(Guid slotId)
    {
        var slot = await GetSlotAsync(slotId);
        if (slot is null || !HasStatus(slot.Status, ScheduleSlotStatus.Overrun)) return;

        var session = await dbContext.FocusSessions
            .FirstOrDefaultAsync(s => s.ScheduleSlotId == slotId && s.EndedAt == null);
        if (session is null) return;

        session.EndedAt = timeProvider.GetUtcNow().UtcDateTime;
        session.CaptureMethod = CaptureMethod.AutoTimeout.ToString();
        session.StopReason = FocusStopReason.Other.ToString();
        session.IsConfirmed = false;
        session.Note = "Hệ thống tự động ngắt do vượt quá 2 lần thời gian dự kiến.";
        slot.Status = ScheduleSlotStatus.Done.ToString();
        await dbContext.SaveChangesAsync();

        await notification.SendAsync(slot.Task.Project.UserId, "Focus session", "Phiên làm việc đã bị ngắt tự động vì quá lâu.", "AUTO_TIMEOUT", slot.Id);
    }

    public async Task MarkUnknown(Guid slotId)
    {
        var slot = await dbContext.ScheduleSlots
            .Include(s => s.FocusSessions)
            .FirstOrDefaultAsync(s => s.Id == slotId);
        if (slot is null) return;

        if (slot.FocusSessions.Any(s => s.CaptureMethod == CaptureMethod.AutoTimeout.ToString() && !s.IsConfirmed))
        {
            slot.Explanation = "IGNORED_BY_USER";
            await dbContext.SaveChangesAsync();
        }
    }

    private Task<ScheduleSlot?> GetSlotAsync(Guid slotId)
    {
        return dbContext.ScheduleSlots
            .Include(s => s.Task)
            .ThenInclude(t => t.Project)
            .FirstOrDefaultAsync(s => s.Id == slotId);
    }

    private static bool HasStatus(string status, ScheduleSlotStatus expected)
    {
        return string.Equals(status, expected.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
