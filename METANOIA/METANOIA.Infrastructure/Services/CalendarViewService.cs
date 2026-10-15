using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;

namespace METANOIA.Infrastructure.Services
{
    public class CalendarViewService : ICalendarService
    {
        private const int MaxRangeDays = 100;
        private const string NotConnected = "NotConnected";

        private readonly ICalendarRepository _calendarRepository;
        private readonly IUserSetupRepository _setupRepository;

        public CalendarViewService(ICalendarRepository calendarRepository, IUserSetupRepository setupRepository)
        {
            _calendarRepository = calendarRepository;
            _setupRepository = setupRepository;
        }

        public async Task<CalendarDto> GetCalendarAsync(
            Guid userId, DateTimeOffset from, DateTimeOffset to, bool includeCancelled, CancellationToken cancellationToken = default)
        {
            if (to <= from)
            {
                throw new UserFriendlyException("Khoảng thời gian không hợp lệ: 'to' phải lớn hơn 'from'.");
            }

            if (to - from > TimeSpan.FromDays(MaxRangeDays))
            {
                throw new UserFriendlyException($"Khoảng thời gian tối đa {MaxRangeDays} ngày.");
            }

            var workingHours = await _calendarRepository.GetWorkingHoursAsync(userId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy người dùng.");
            var slots = await _calendarRepository.GetSlotsAsync(userId, from.UtcDateTime, to.UtcDateTime, includeCancelled, cancellationToken);
            var events = await _calendarRepository.GetGoogleEventsAsync(userId, from.UtcDateTime, to.UtcDateTime, cancellationToken);
            var googleStatus = await _setupRepository.GetGoogleConnectionStatusAsync(userId, cancellationToken);

            var items = new List<CalendarItemDto>();

            items.AddRange(slots.Select(slot => new CalendarItemDto
            {
                Source = "Slot",
                Id = slot.Id.ToString(),
                Title = slot.TaskTitle,
                Start = UtcTime.ToApi(slot.StartTime),
                End = UtcTime.ToApi(slot.EndTime),
                Status = slot.Status,
                TaskId = slot.TaskId,
                SubTaskId = slot.SubTaskId,
                SubTaskTitle = slot.SubTaskTitle,
                ProjectId = slot.ProjectId,
                ProjectName = slot.ProjectName,
                ClientName = slot.ClientName,
                DomainName = slot.DomainName,
                TaskType = slot.TaskType,
                TaskStatus = slot.TaskStatus,
                TaskDeadline = UtcTime.ToApi(slot.TaskDeadline),
                ProposedMinutes = slot.ProposedMinutes,
                Explanation = slot.Explanation,
                IsManualOverride = slot.IsManualOverride,
                SyncFailed = slot.SyncFailed
            }));

            items.AddRange(events.Select(e => new CalendarItemDto
            {
                Source = "Google",
                Id = e.GoogleEventId,
                Title = e.Title,
                Start = UtcTime.ToApi(e.StartTime),
                End = UtcTime.ToApi(e.EndTime),
                IsAllDay = e.IsAllDay
            }));

            return new CalendarDto
            {
                From = from,
                To = to,
                GoogleCalendarStatus = string.IsNullOrWhiteSpace(googleStatus) ? NotConnected : googleStatus,
                TimeZone = workingHours.TimeZone,
                WorkDayStart = workingHours.WorkDayStart,
                WorkDayEnd = workingHours.WorkDayEnd,
                WorksOnWeekend = workingHours.WorksOnWeekend,
                Items = items.OrderBy(i => i.Start).ThenBy(i => i.End).ToList()
            };
        }
    }
}
