using System.Net;
using System.Text;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.DataProtection;
using GoogleEvent = Google.Apis.Calendar.v3.Data.Event;

namespace METANOIA.Infrastructure.Services
{
    public class GoogleCalendarEventService : IGoogleCalendarEventService
    {
        private const string CalendarId = "primary";

        private readonly IGoogleConnectionRepository _connectionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly GoogleOAuthClient _oauthClient;
        private readonly IDataProtector _tokenProtector;

        public GoogleCalendarEventService(
            IGoogleConnectionRepository connectionRepository,
            IUnitOfWork unitOfWork,
            GoogleOAuthClient oauthClient,
            IDataProtectionProvider dataProtectionProvider)
        {
            _connectionRepository = connectionRepository;
            _unitOfWork = unitOfWork;
            _oauthClient = oauthClient;
            _tokenProtector = dataProtectionProvider.CreateProtector("GoogleCalendar.Tokens");
        }

        public async Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(Guid userId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            var service = await CreateCalendarServiceAsync(userId, cancellationToken);

            var request = service.Events.List(CalendarId);
            request.TimeMinDateTimeOffset = from;
            request.TimeMaxDateTimeOffset = to;
            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

            var result = await ExecuteAsync(() => request.ExecuteAsync(cancellationToken));
            return (result.Items ?? []).Select(ToDto).ToList();
        }

        public async Task<CalendarEventDto> GetEventAsync(Guid userId, string eventId, CancellationToken cancellationToken = default)
        {
            var service = await CreateCalendarServiceAsync(userId, cancellationToken);
            var googleEvent = await ExecuteAsync(() => service.Events.Get(CalendarId, eventId).ExecuteAsync(cancellationToken));
            return ToDto(googleEvent);
        }

        public async Task<CalendarEventDto> CreateEventAsync(Guid userId, CalendarEventRequestDto request, CancellationToken cancellationToken = default)
        {
            ValidateRequest(request);

            var service = await CreateCalendarServiceAsync(userId, cancellationToken);
            var created = await service.Events.Insert(ToGoogleEvent(request), CalendarId).ExecuteAsync(cancellationToken);
            return ToDto(created);
        }

        public async Task<CalendarEventDto> UpdateEventAsync(Guid userId, string eventId, CalendarEventRequestDto request, CancellationToken cancellationToken = default)
        {
            ValidateRequest(request);

            var service = await CreateCalendarServiceAsync(userId, cancellationToken);
            var updated = await ExecuteAsync(() =>
                service.Events.Update(ToGoogleEvent(request), CalendarId, eventId).ExecuteAsync(cancellationToken));
            return ToDto(updated);
        }

        public async Task DeleteEventAsync(Guid userId, string eventId, CancellationToken cancellationToken = default)
        {
            var service = await CreateCalendarServiceAsync(userId, cancellationToken);
            await ExecuteAsync(() => service.Events.Delete(CalendarId, eventId).ExecuteAsync(cancellationToken));
        }

        private async Task<CalendarService> CreateCalendarServiceAsync(Guid userId, CancellationToken cancellationToken)
        {
            var accessToken = await GetValidAccessTokenAsync(userId, cancellationToken);

            return new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
                ApplicationName = "METANOIA"
            });
        }

        private async Task<string> GetValidAccessTokenAsync(Guid userId, CancellationToken cancellationToken)
        {
            var connection = await _connectionRepository.GetByUserIdAsync(userId, cancellationToken);
            if (connection is null || connection.Status != "Active")
            {
                throw new GoogleCalendarNotConnectedException();
            }

            if (connection.TokenExpiresAt > DateTime.UtcNow.AddMinutes(1))
            {
                return Encoding.UTF8.GetString(_tokenProtector.Unprotect(connection.AccessTokenEncrypted));
            }

            var refreshToken = Encoding.UTF8.GetString(_tokenProtector.Unprotect(connection.RefreshTokenEncrypted));
            var refreshed = await _oauthClient.RefreshAccessTokenAsync(refreshToken, cancellationToken);
            if (refreshed is null)
            {
                connection.Status = "Revoked";
                connection.LastSyncError = "Google từ chối refresh token, có thể user đã thu hồi quyền.";
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                throw new GoogleCalendarNotConnectedException();
            }

            connection.AccessTokenEncrypted = _tokenProtector.Protect(Encoding.UTF8.GetBytes(refreshed.AccessToken));
            // Cột timestamp không có time zone: Npgsql từ chối DateTime có Kind = Utc
            connection.TokenExpiresAt = DateTime.SpecifyKind(
                DateTime.UtcNow.AddSeconds(refreshed.ExpiresIn), DateTimeKind.Unspecified);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return refreshed.AccessToken;
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
