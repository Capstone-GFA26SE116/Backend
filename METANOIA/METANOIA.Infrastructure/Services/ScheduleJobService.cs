using Hangfire;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using System.Linq.Expressions;

namespace METANOIA.Infrastructure.Services;

public sealed class ScheduleJobService(
    IBackgroundJobClient jobClient,
    AppDbContext dbContext) : IScheduleJobService
{
    public void ScheduleAllJobsForSlot(ScheduleSlot slot)
    {
        CancelAllJobs(slot.Id);

        var start = new DateTimeOffset(DateTime.SpecifyKind(slot.StartTime, DateTimeKind.Utc));
        var end = new DateTimeOffset(DateTime.SpecifyKind(slot.EndTime, DateTimeKind.Utc));
        var autoTimeout = start.AddMinutes(slot.ProposedMinutes * 2);

        ScheduleJob(slot.Id, "PreStartReminder", start.AddMinutes(-15), x => x.PreStartReminder(slot.Id));
        ScheduleJob(slot.Id, "SlotDue", start, x => x.SlotDue(slot.Id));
        ScheduleJob(slot.Id, "NotStartedNudge", start.AddMinutes(20), x => x.NotStartedNudge(slot.Id));
        ScheduleJob(slot.Id, "NearEndReminder", end.AddMinutes(-10), x => x.NearEndReminder(slot.Id));
        ScheduleJob(slot.Id, "SlotEnded", end, x => x.SlotEnded(slot.Id));
        ScheduleJob(slot.Id, "OverrunReminder", end.AddMinutes(15), x => x.OverrunReminder(slot.Id));
        ScheduleJob(slot.Id, "CheckInRetry", end.AddMinutes(30), x => x.CheckInRetry(slot.Id));
        ScheduleJob(slot.Id, "AutoTimeout", autoTimeout, x => x.AutoTimeout(slot.Id));
        ScheduleJob(slot.Id, "MarkUnknown", end.AddHours(24), x => x.MarkUnknown(slot.Id));
    }

    public void CancelAllJobs(Guid slotId)
    {
        var jobs = dbContext.HangfireJobTrackers
            .Where(x => x.ScheduleSlotId == slotId)
            .ToList();

        foreach (var job in jobs)
            BackgroundJob.Delete(job.HangfireJobId);

        if (jobs.Count > 0)
        {
            dbContext.HangfireJobTrackers.RemoveRange(jobs);
            dbContext.SaveChanges();
        }
    }

    public void CancelOverrunJobs(Guid slotId)
    {
        var types = new[] { "NearEndReminder", "OverrunReminder", "AutoTimeout" };
        var jobs = dbContext.HangfireJobTrackers
            .Where(x => x.ScheduleSlotId == slotId && types.Contains(x.JobType))
            .ToList();

        foreach (var job in jobs)
            BackgroundJob.Delete(job.HangfireJobId);

        if (jobs.Count > 0)
        {
            dbContext.HangfireJobTrackers.RemoveRange(jobs);
            dbContext.SaveChanges();
        }
    }

    public void RescheduleSlotJobs(Guid slotId, DateTimeOffset actualStartTime)
    {
        var slot = dbContext.ScheduleSlots.Find(slotId);
        if (slot is null)
            return;

        var duration = slot.EndTime - slot.StartTime;
        slot.StartTime = actualStartTime.UtcDateTime;
        slot.EndTime = actualStartTime.UtcDateTime.Add(duration);
        ScheduleAllJobsForSlot(slot);
    }

    private void ScheduleJob(
        Guid slotId,
        string jobType,
        DateTimeOffset executeAt,
        Expression<Action<SlotBackgroundJobs>> methodCall)
    {
        if (executeAt <= DateTimeOffset.UtcNow)
            return;

        var jobId = jobClient.Schedule(methodCall, executeAt);
        dbContext.HangfireJobTrackers.Add(new HangfireJobTracker
        {
            Id = Guid.NewGuid(),
            ScheduleSlotId = slotId,
            JobType = jobType,
            HangfireJobId = jobId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.SaveChanges();
    }
}
