using Hangfire;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;

namespace METANOIA.Infrastructure.Services;

public sealed class ScheduleJobService(
    IBackgroundJobClient jobClient,
    AppDbContext dbContext,
    ILogger<ScheduleJobService> logger) : IScheduleJobService
{
    private static readonly string[] OverrunJobTypes = ["NearEndReminder", "OverrunReminder", "AutoTimeout"];

    private sealed record PlannedJob(string JobType, DateTimeOffset RunAt, Expression<Action<SlotBackgroundJobs>> Call);

    public void ScheduleAllJobsForSlot(ScheduleSlot slot)
    {
        var slotId = slot.Id;
        var start = AsUtc(slot.StartTime);
        var end = AsUtc(slot.EndTime);
        var planned = Math.Max(slot.ProposedMinutes, 1);

        Run(slotId, "hẹn toàn bộ job", () =>
        {
            RemoveJobs(slotId, null);
            Schedule(slotId,
            [
                new("PreStartReminder", start.AddMinutes(-15), x => x.PreStartReminder(slotId)),
                new("SlotDue", start, x => x.SlotDue(slotId)),
                new("NotStartedNudge", start.AddMinutes(20), x => x.NotStartedNudge(slotId)),
                new("NearEndReminder", end.AddMinutes(-10), x => x.NearEndReminder(slotId)),
                new("SlotEnded", end, x => x.SlotEnded(slotId)),
                new("OverrunReminder", end.AddMinutes(15), x => x.OverrunReminder(slotId)),
                new("CheckInRetry", end.AddMinutes(30), x => x.CheckInRetry(slotId)),
                new("AutoTimeout", start.AddMinutes(planned * 2), x => x.AutoTimeout(slotId)),
                new("MarkUnknown", end.AddHours(24), x => x.MarkUnknown(slotId))
            ]);
        });
    }

    public void RescheduleSlotJobs(Guid slotId, DateTimeOffset actualStartTime)
    {
        Run(slotId, "hẹn lại job sau khi bấm Start", () =>
        {
            var slot = dbContext.ScheduleSlots.Find(slotId);
            if (slot is null)
            {
                return;
            }

            var planned = Math.Max(slot.ProposedMinutes, 1);
            var actualStart = actualStartTime.ToUniversalTime();
            var expectedEnd = actualStart.AddMinutes(planned);

            RemoveJobs(slotId, null);
            Schedule(slotId,
            [
                new("NearEndReminder", expectedEnd.AddMinutes(-10), x => x.NearEndReminder(slotId)),
                new("SlotEnded", expectedEnd, x => x.SlotEnded(slotId)),
                new("OverrunReminder", expectedEnd.AddMinutes(15), x => x.OverrunReminder(slotId)),
                new("CheckInRetry", expectedEnd.AddMinutes(30), x => x.CheckInRetry(slotId)),
                new("AutoTimeout", actualStart.AddMinutes(planned * 2), x => x.AutoTimeout(slotId)),
                new("MarkUnknown", expectedEnd.AddHours(24), x => x.MarkUnknown(slotId))
            ]);
        });
    }

    public void CancelOverrunJobs(Guid slotId)
    {
        Run(slotId, "hủy job quá giờ", () => RemoveJobs(slotId, OverrunJobTypes));
    }

    public void CancelAllJobs(Guid slotId)
    {
        Run(slotId, "hủy mọi job", () => RemoveJobs(slotId, null));
    }

    // Xóa job trên Hangfire và dòng theo dõi tương ứng. types = null nghĩa là mọi loại
    private void RemoveJobs(Guid slotId, string[]? types)
    {
        var trackers = dbContext.HangfireJobTrackers
            .Where(x => x.ScheduleSlotId == slotId && (types == null || types.Contains(x.JobType)))
            .ToList();

        if (trackers.Count == 0)
        {
            return;
        }

        foreach (var tracker in trackers)
        {
            jobClient.Delete(tracker.HangfireJobId);
        }

        dbContext.HangfireJobTrackers.RemoveRange(trackers);
        dbContext.SaveChanges();
    }

    // Hẹn các job còn ở tương lai và ghi theo dõi bằng một lần lưu
    private void Schedule(Guid slotId, IEnumerable<PlannedJob> jobs)
    {
        var now = DateTimeOffset.UtcNow;
        var added = 0;

        foreach (var job in jobs.Where(j => j.RunAt > now))
        {
            var hangfireJobId = jobClient.Schedule(job.Call, job.RunAt);
            dbContext.HangfireJobTrackers.Add(new HangfireJobTracker
            {
                Id = Guid.NewGuid(),
                ScheduleSlotId = slotId,
                JobType = job.JobType,
                HangfireJobId = hangfireJobId,
                CreatedAt = now
            });
            added++;
        }

        if (added > 0)
        {
            dbContext.SaveChanges();
        }
    }

    private void Run(Guid slotId, string action, Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex)
        {
            // Hangfire hoặc bảng HangfireJobTrackers gặp sự cố thì request chính vẫn thành công, chỉ thiếu nhắc nhở
            logger.LogError(ex, "Không {Action} cho slot {SlotId}.", action, slotId);

            // Bỏ các dòng theo dõi chưa lưu được để không làm hỏng lần SaveChanges sau của request
            foreach (var entry in dbContext.ChangeTracker.Entries<HangfireJobTracker>()
                         .Where(e => e.State == EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private static DateTimeOffset AsUtc(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);
    }
}
