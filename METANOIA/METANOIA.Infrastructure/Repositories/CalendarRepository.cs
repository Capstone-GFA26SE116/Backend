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
                .Where(s => s.Task.Project.UserId == userId && s.StartTime < to && s.EndTime > from);

            if (!includeCancelled)
            {
                query = query.Where(s => s.Status != "Cancelled");
            }

            // Một câu SELECT duy nhất: slot join task, subtask (LEFT), project, client (LEFT), domain. Không truy vấn lặp theo từng slot
            return await query
                .OrderBy(s => s.StartTime)
                .Select(s => new CalendarSlotRow
                {
                    Id = s.Id,
                    TaskId = s.TaskId,
                    SubTaskId = s.SubTaskId,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    Status = s.Status,
                    ProposedMinutes = s.ProposedMinutes,
                    Explanation = s.Explanation,
                    IsManualOverride = s.IsManualOverride,
                    SyncFailed = s.SyncFailed,
                    TaskTitle = s.Task.Title,
                    TaskType = s.Task.TaskType,
                    TaskStatus = s.Task.Status,
                    TaskDeadline = s.Task.Deadline,
                    SubTaskTitle = s.SubTask != null ? s.SubTask.Title : null,
                    ProjectId = s.Task.ProjectId,
                    ProjectName = s.Task.Project.Name,
                    ClientName = s.Task.Project.Client != null ? s.Task.Project.Client.Name : null,
                    DomainName = s.Task.Domain.Name
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
