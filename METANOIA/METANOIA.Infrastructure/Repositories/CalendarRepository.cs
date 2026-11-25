using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace METANOIA.Infrastructure.Repositories
{
    public class CalendarRepository : ICalendarRepository
    {
        private readonly AppDbContext _context;

        public CalendarRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<CalendarSlotRow>> GetSlotsAsync(
            Guid userId, DateTime from, DateTime to, bool includeCancelled, CancellationToken cancellationToken = default)
        {
            var query = _context.ScheduleSlots
                .OwnedBy(userId)
                .Where(s => s.StartTime < to && s.EndTime > from);

            if (!includeCancelled)
            {
                query = query.Where(s => s.Status != nameof(METANOIA.Domain.Enums.ScheduleSlotStatus.Cancelled));
            }

            // Một câu SELECT duy nhất: slot join task, subtask (LEFT), project, client (LEFT), domain. Không truy vấn lặp theo từng slot
            return await query
                .OrderBy(s => s.StartTime)
                .Select(s => new CalendarSlotRow
                {
                    Id = s.Id,
                    TaskId = s.TaskId,
                    SubTaskId = s.SubTaskId,
                    GroupId = s.GroupTask != null ? s.GroupTask.GroupId : null,
                    GroupName = s.GroupTask != null ? s.GroupTask.Group.Name : null,
                    GroupTaskId = s.GroupTaskId,
                    GroupSubTaskId = s.GroupSubTaskId,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    Status = s.Status,
                    ProposedMinutes = s.ProposedMinutes,
                    Explanation = s.Explanation,
                    IsManualOverride = s.IsManualOverride,
                    SyncFailed = s.SyncFailed,
                    TaskTitle = s.Task != null ? s.Task.Title : s.GroupTask!.Title,
                    TaskType = s.Task != null ? s.Task.TaskType : null,
                    TaskStatus = s.Task != null ? s.Task.Status : s.GroupTask!.Status,
                    TaskDeadline = s.Task != null ? s.Task.Deadline : s.GroupTask!.Deadline,
                    SubTaskTitle = s.SubTask != null ? s.SubTask.Title : (s.GroupSubTask != null ? s.GroupSubTask.Title : null),
                    ProjectId = s.Task != null ? s.Task.ProjectId : null,
                    ProjectName = s.Task != null ? s.Task.Project.Name : null,
                    ClientName = s.Task != null && s.Task.Project.Client != null ? s.Task.Project.Client.Name : null,
                    DomainName = s.Task != null ? s.Task.Domain.Name : null
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<GoogleCalendarEvent>> GetGoogleEventsAsync(
            Guid userId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            return await _context.GoogleCalendarEvents
                .Where(e => e.GoogleConnection.UserId == userId
                    && !e.IsCancelled
                    && !e.IsCreatedByMetanoia
                    && e.StartTime < to
                    && e.EndTime > from)
                .OrderBy(e => e.StartTime)
                .ToListAsync(cancellationToken);
        }

        public Task<CalendarWorkingHours?> GetWorkingHoursAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Users
                .Where(u => u.Id == userId)
                .Select(u => new CalendarWorkingHours
                {
                    TimeZone = u.TimeZone,
                    WorkDayStart = u.WorkDayStart,
                    WorkDayEnd = u.WorkDayEnd,
                    WorksOnWeekend = u.WorksOnWeekend
                })
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
