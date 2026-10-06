using System.Net;
using Google;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using GoogleEvent = Google.Apis.Calendar.v3.Data.Event;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class GoogleCalendarEventService : IGoogleCalendarEventService
    {
        private const string CalendarId = "primary";

        private readonly GoogleCalendarClientProvider _provider;
        private readonly IGoogleCalendarEventRepository _eventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public GoogleCalendarEventService(
            GoogleCalendarClientProvider provider,
            IGoogleCalendarEventRepository eventRepository,
            IUnitOfWork unitOfWork)
        {
            _provider = provider;
            _eventRepository = eventRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(Guid userId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            var session = await _provider.OpenAsync(userId, cancellationToken);

            var request = session.Service.Events.List(CalendarId);
            request.TimeMinDateTimeOffset = from;
            request.TimeMaxDateTimeOffset = to;
            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

            var result = await ExecuteAsync(() => request.ExecuteAsync(cancellationToken));
            return (result.Items ?? []).Select(ToDto).ToList();
        }

        public async Task<CalendarEventDto> GetEventAsync(Guid userId, string eventId, CancellationToken cancellationToken = default)
        {
            var session = await _provider.OpenAsync(userId, cancellationToken);
            var googleEvent = await ExecuteAsync(() => session.Service.Events.Get(CalendarId, eventId).ExecuteAsync(cancellationToken));
            return ToDto(googleEvent);
        }

        public async Task<CalendarEventDto> CreateEventAsync(Guid userId, CalendarEventRequestDto request, CancellationToken cancellationToken = default)
        {
            ValidateRequest(request);

            var session = await _provider.OpenAsync(userId, cancellationToken);
            var created = await ExecuteAsync(() => session.Service.Events.Insert(ToGoogleEvent(request), CalendarId).ExecuteAsync(cancellationToken));

            await UpsertLocalAsync(session.Connection, created, isCreatedByMetanoia: true, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(created);
        }

        public async Task<CalendarEventDto> UpdateEventAsync(Guid userId, string eventId, CalendarEventRequestDto request, CancellationToken cancellationToken = default)
        {
            ValidateRequest(request);

            var session = await _provider.OpenAsync(userId, cancellationToken);
            var updated = await ExecuteAsync(() =>
                session.Service.Events.Update(ToGoogleEvent(request), CalendarId, eventId).ExecuteAsync(cancellationToken));

            await UpsertLocalAsync(session.Connection, updated, isCreatedByMetanoia: false, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(updated);
        }

        public async Task DeleteEventAsync(Guid userId, string eventId, CancellationToken cancellationToken = default)
        {
            var session = await _provider.OpenAsync(userId, cancellationToken);
            await ExecuteAsync(() => session.Service.Events.Delete(CalendarId, eventId).ExecuteAsync(cancellationToken));

            var local = await _eventRepository.GetByGoogleEventIdAsync(session.Connection.Id, eventId, cancellationToken);
            if (local is not null)
            {
                local.IsCancelled = true;
                local.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        // isCreatedByMetanoia chỉ dùng khi tạo dòng mới; dòng đã có giữ nguyên cờ
        private async Task UpsertLocalAsync(GoogleConnection connection, GoogleEvent googleEvent, bool isCreatedByMetanoia, CancellationToken cancellationToken)
        {
            var local = await _eventRepository.GetByGoogleEventIdAsync(connection.Id, googleEvent.Id, cancellationToken);
            if (local is null)
            {
                local = new GoogleCalendarEvent
                {
                    Id = Guid.NewGuid(),
                    GoogleConnectionId = connection.Id,
                    GoogleEventId = googleEvent.Id,
                    IsCreatedByMetanoia = isCreatedByMetanoia
                };
                await _eventRepository.AddAsync(local, cancellationToken);
            }

            GoogleEventMapping.Apply(local, googleEvent);
        }

        private static async Task<T> ExecuteAsync<T>(Func<Task<T>> call)
        {
            try
            {
                return await call();
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                throw new NotFoundException("Không tìm thấy sự kiện trên Google Calendar.", ex);
            }
            catch (GoogleApiException ex)
            {
                throw new UserFriendlyException(
                    $"Google Calendar trả lỗi {(int)ex.HttpStatusCode}: {ex.Message}. " +
                    "Kiểm tra Google Calendar API đã được bật cho project và tài khoản đã cấp đủ quyền.", ex);
            }
        }

        private static void ValidateRequest(CalendarEventRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                throw new UserFriendlyException("Tiêu đề sự kiện không được để trống.");
            }

            if (request.End <= request.Start)
            {
                throw new UserFriendlyException("Thời gian kết thúc phải sau thời gian bắt đầu.");
            }
        }

        private static GoogleEvent ToGoogleEvent(CalendarEventRequestDto request)
        {
            return new GoogleEvent
            {
                Summary = request.Title,
                Description = request.Description,
                Location = request.Location,
                Start = new EventDateTime { DateTimeDateTimeOffset = request.Start },
                End = new EventDateTime { DateTimeDateTimeOffset = request.End }
            };
        }

        private static CalendarEventDto ToDto(GoogleEvent googleEvent)
        {
            return new CalendarEventDto
            {
                Id = googleEvent.Id,
                Title = googleEvent.Summary,
                Description = googleEvent.Description,
                Location = googleEvent.Location,
                Start = ToDateTimeOffset(googleEvent.Start),
                End = ToDateTimeOffset(googleEvent.End),
                HtmlLink = googleEvent.HtmlLink
            };
        }

        // Sự kiện cả ngày chỉ có Date (yyyy-MM-dd), không có DateTime
        private static DateTimeOffset ToDateTimeOffset(EventDateTime value)
        {
            return value.DateTimeDateTimeOffset
                ?? new DateTimeOffset(DateOnly.Parse(value.Date!).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        }
    }
}
