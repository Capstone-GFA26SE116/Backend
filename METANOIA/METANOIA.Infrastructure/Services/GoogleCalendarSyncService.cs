using System.Net;
using System.Security.Cryptography;
using Google;
using Google.Apis.Calendar.v3.Data;
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class GoogleCalendarSyncService : IGoogleCalendarSyncService
    {
        private const string CalendarId = "primary";
        private const int PageSize = 250;
        private const int MaxLastSyncErrorLength = 500;
        private const int MaxPushErrors = 5;

        // Slot được đẩy lên Google: đã duyệt và đang làm
        private static readonly string[] LiveSlotStatuses = ["Approved", "Due", "InProgress", "Overrun"];

        // Slot đã đẩy mà sau đó không còn diễn ra ở giờ đó: xóa sự kiện trên Google (Done và Missed giữ lại như lịch sử)
        private static readonly string[] RemovedSlotStatuses = ["Cancelled", "Skipped", "Postponed"];

        // Khoảng thời gian UTC: FromUtc gồm, ToUtc không gồm. null là không giới hạn phía đó
        private sealed record SyncRange(DateTime? FromUtc, DateTime? ToUtc);

        private readonly GoogleCalendarClientProvider _provider;
        private readonly IGoogleConnectionRepository _connectionRepository;
        private readonly IGoogleCalendarEventRepository _eventRepository;
        private readonly IScheduleSlotRepository _slotRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<GoogleCalendarSyncService> _logger;

        public GoogleCalendarSyncService(
            GoogleCalendarClientProvider provider,
            IGoogleConnectionRepository connectionRepository,
            IGoogleCalendarEventRepository eventRepository,
            IScheduleSlotRepository slotRepository,
            IUnitOfWork unitOfWork,
            ILogger<GoogleCalendarSyncService> logger)
        {
            _provider = provider;
            _connectionRepository = connectionRepository;
            _eventRepository = eventRepository;
            _slotRepository = slotRepository;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public Task<GoogleCalendarSyncResultDto> SyncAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return SyncAsync(userId, new GoogleCalendarSyncRequest(), cancellationToken);
        }

        // Kết quả cho người gọi:
        //   chưa có GoogleConnection            -> 400 "Chưa kết nối Google Calendar"
        //   To trước From                       -> 400
        //   Expired/Revoked, invalid_grant,
        //   không giải mã được token đã lưu     -> 409 "Cần kết nối lại Google Calendar" (không lộ chi tiết)
        //   Google lỗi / quá hạn / mất mạng     -> ghi LastSyncError, 502
        // Mọi exception đều được LogError trước khi trả về.
        public async Task<GoogleCalendarSyncResultDto> SyncAsync(Guid userId, GoogleCalendarSyncRequest request, CancellationToken cancellationToken = default)
        {
            if (request.From is { } from && request.To is { } to && to < from)
            {
                throw new UserFriendlyException("Ngày kết thúc (to) phải từ ngày bắt đầu (from) trở đi.");
            }

            var connection = await _connectionRepository.GetByUserIdAsync(userId, cancellationToken);
            if (connection is null)
            {
                var notConnected = new UserFriendlyException("Chưa kết nối Google Calendar.");
                _logger.LogError(notConnected, "Đồng bộ Google Calendar thất bại: user {UserId} chưa có GoogleConnection.", userId);
                throw notConnected;
            }

            if (connection.Status != "Active")
            {
                var reconnect = new GoogleReconnectRequiredException();
                _logger.LogError(reconnect, "Đồng bộ Google Calendar thất bại: kết nối của user {UserId} đang ở trạng thái {Status}.", userId, connection.Status);
                throw reconnect;
            }

            var range = await ResolveRangeAsync(userId, request, cancellationToken);

            try
            {
                var session = await _provider.OpenAsync(userId, cancellationToken);
                return await RunAsync(session, request, range, cancellationToken);
            }
            catch (GoogleCalendarNotConnectedException ex)
            {
                // Provider thấy Google trả invalid_grant và đã đặt Status = Revoked
                _logger.LogError(ex, "Google từ chối refresh token của user {UserId} (invalid_grant), kết nối đã bị thu hồi.", userId);
                throw new GoogleReconnectRequiredException();
            }
            catch (CryptographicException ex)
            {
                // Thường do khóa DataProtection khác với lúc lưu token (đổi máy chủ, khởi động lại khi khóa không được lưu bền).
                // KHÔNG đổi Status: kết nối này có thể vẫn dùng được ở môi trường đã mã hóa nó.
                _logger.LogError(ex, "Không giải mã được token Google của user {UserId}; cần kết nối lại Google Calendar.", userId);
                throw new GoogleReconnectRequiredException();
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogError(ex, "Google Calendar API từ chối access token của user {UserId} (401), kết nối bị thu hồi.", userId);
                connection.Status = "Revoked";
                connection.LastSyncError = Trim($"Google Calendar API từ chối quyền truy cập (401): {ex.Message}");
                await TrySaveAsync(cancellationToken);
                throw new GoogleReconnectRequiredException();
            }
            catch (GoogleApiException ex)
            {
                _logger.LogError(ex, "Google Calendar API lỗi ({StatusCode}) khi đồng bộ cho user {UserId}.", (int)ex.HttpStatusCode, userId);
                await RecordFailureAsync(connection, $"Google Calendar API {(int)ex.HttpStatusCode}: {ex.Message}", cancellationToken);
                throw new GoogleApiFailureException("Google Calendar API lỗi.", ex);
            }
            catch (GoogleApiFailureException ex)
            {
                _logger.LogError(ex, "Lỗi từ Google khi lấy token cho user {UserId}.", userId);
                await RecordFailureAsync(connection, ex.Message, cancellationToken);
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                _logger.LogError(ex, "Không liên lạc được với Google (mạng hoặc quá thời gian chờ) khi đồng bộ cho user {UserId}.", userId);
                await RecordFailureAsync(connection, $"Không liên lạc được với Google: {ex.Message}", cancellationToken);
                throw new GoogleApiFailureException("Không liên lạc được với Google.", ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Lỗi không lường trước: vẫn phải ghi log lỗi, rồi để middleware trả 500 chung
                _logger.LogError(ex, "Lỗi không xác định khi đồng bộ Google Calendar cho user {UserId}.", userId);
                throw;
            }
        }

        private async Task<GoogleCalendarSyncResultDto> RunAsync(
            GoogleCalendarSession session, GoogleCalendarSyncRequest request, SyncRange? range, CancellationToken cancellationToken)
        {
            var result = new GoogleCalendarSyncResultDto
            {
                Direction = request.Direction,
                Mode = range is null ? "Incremental" : "Range",
                RangeFromUtc = range?.FromUtc is { } f ? new DateTimeOffset(f, TimeSpan.Zero) : null,
                RangeToUtc = range?.ToUtc is { } t ? new DateTimeOffset(t, TimeSpan.Zero) : null
            };

            // Google -> METANOIA trước, rồi METANOIA -> Google
            if (request.Direction != GoogleSyncDirection.ToGoogle)
            {
                if (range is null)
                {
                    try
                    {
                        await RunSyncAsync(session, result, cancellationToken);
                    }
                    catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Gone)
                    {
                        // syncToken đã hết hạn: Google yêu cầu đồng bộ lại từ đầu
                        _logger.LogError(ex, "syncToken của user {UserId} đã hết hạn, đồng bộ lại từ đầu.", session.Connection.UserId);
                        session.Connection.SyncToken = null;
                        await RunSyncAsync(session, result, cancellationToken);
                    }
                }
                else
                {
                    await RunRangeSyncAsync(session, range, result, cancellationToken);
                }
            }

            if (request.Direction != GoogleSyncDirection.FromGoogle)
            {
                await PushSlotsAsync(session, range, result, cancellationToken);
            }

            return result;
        }

        // Ngày From, To theo múi giờ của người dùng; To gồm trọn ngày đó
        private async Task<SyncRange?> ResolveRangeAsync(Guid userId, GoogleCalendarSyncRequest request, CancellationToken cancellationToken)
        {
            if (request.From is null && request.To is null)
            {
                return null;
            }

            var zone = ResolveTimeZone(await _connectionRepository.GetUserTimeZoneAsync(userId, cancellationToken));
            return new SyncRange(
                request.From is { } from ? StartOfDayUtc(from, zone) : null,
                request.To is { } to ? StartOfDayUtc(to.AddDays(1), zone) : null);
        }

        private static TimeZoneInfo ResolveTimeZone(string? id)
        {
            try
            {
                return string.IsNullOrWhiteSpace(id) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return TimeZoneInfo.Utc;
            }
        }

        private static DateTime StartOfDayUtc(DateOnly date, TimeZoneInfo zone)
        {
            var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);

            // Một số múi giờ nhảy giờ đúng lúc nửa đêm: lùi sang giờ hợp lệ gần nhất
            if (zone.IsInvalidTime(local))
            {
                local = local.AddHours(1);
            }

            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }

        private async Task RecordFailureAsync(GoogleConnection connection, string message, CancellationToken cancellationToken)
        {
            connection.LastSyncError = Trim(message);
            await TrySaveAsync(cancellationToken);
        }

        // Ghi trạng thái lỗi chỉ là phụ: nếu chính việc lưu này lỗi thì log lại, không che mất lỗi gốc đang được xử lý
        private async Task TrySaveAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Không lưu được trạng thái đồng bộ Google Calendar.");
            }
        }

        private static string Trim(string value)
        {
            return value.Length <= MaxLastSyncErrorLength ? value : value[..MaxLastSyncErrorLength];
        }

        // ---------------------------------------------------------------- Google -> METANOIA

        private async Task RunSyncAsync(GoogleCalendarSession session, GoogleCalendarSyncResultDto result, CancellationToken cancellationToken)
        {
            var connection = session.Connection;
            string? pageToken = null;
            string? nextSyncToken = null;

            do
            {
                // Các tham số phải giữ nguyên giữa các lần sync để syncToken hợp lệ
                var request = session.Service.Events.List(CalendarId);
                request.ShowDeleted = true;
                request.MaxResults = PageSize;
                request.SyncToken = connection.SyncToken;
                request.PageToken = pageToken;

                var page = await request.ExecuteAsync(cancellationToken);
                foreach (var googleEvent in page.Items ?? [])
                {
                    await ApplyAsync(connection, googleEvent, result, cancellationToken);
                }

                pageToken = page.NextPageToken;
                nextSyncToken = page.NextSyncToken;
            } while (pageToken is not null);

            connection.SyncToken = nextSyncToken;
            connection.LastSyncedAt = DateTime.UtcNow;
            connection.LastSyncError = null;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // Có chọn ngày: lấy đầy đủ sự kiện trong khoảng. Google không cho dùng syncToken cùng timeMin/timeMax,
        // nên lần này KHÔNG đọc và KHÔNG ghi SyncToken (đồng bộ tự động theo webhook vẫn chạy bình thường)
        private async Task RunRangeSyncAsync(GoogleCalendarSession session, SyncRange range, GoogleCalendarSyncResultDto result, CancellationToken cancellationToken)
        {
            var connection = session.Connection;
            string? pageToken = null;

            do
            {
                var request = session.Service.Events.List(CalendarId);
                request.ShowDeleted = true;
                request.MaxResults = PageSize;
                request.PageToken = pageToken;
                if (range.FromUtc is { } from) request.TimeMinDateTimeOffset = new DateTimeOffset(from, TimeSpan.Zero);
                if (range.ToUtc is { } to) request.TimeMaxDateTimeOffset = new DateTimeOffset(to, TimeSpan.Zero);

                var page = await request.ExecuteAsync(cancellationToken);
                foreach (var googleEvent in page.Items ?? [])
                {
                    await ApplyAsync(connection, googleEvent, result, cancellationToken);
                }

                pageToken = page.NextPageToken;
            } while (pageToken is not null);

            connection.LastSyncedAt = DateTime.UtcNow;
            connection.LastSyncError = null;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task ApplyAsync(GoogleConnection connection, Event googleEvent, GoogleCalendarSyncResultDto result, CancellationToken cancellationToken)
        {
            var local = await _eventRepository.GetByGoogleEventIdAsync(connection.Id, googleEvent.Id, cancellationToken);

            if (googleEvent.Status == "cancelled")
            {
                if (local is not null && !local.IsCancelled)
                {
                    local.IsCancelled = true;
                    local.UpdatedAt = DateTime.UtcNow;
                    result.Cancelled++;
                }
                return;
            }

            if (local is null)
            {
                local = new GoogleCalendarEvent
                {
                    Id = Guid.NewGuid(),
                    GoogleConnectionId = connection.Id,
                    GoogleEventId = googleEvent.Id,
                    // Sự kiện METANOIA đã đẩy lên (id có tiền tố riêng) kéo về thì vẫn là của METANOIA, không hiện trùng với slot
                    IsCreatedByMetanoia = GoogleEventIds.IsFromMetanoia(googleEvent.Id)
                };
                await _eventRepository.AddAsync(local, cancellationToken);
                result.Added++;
            }
            else
            {
                result.Updated++;
            }

            GoogleEventMapping.Apply(local, googleEvent);
        }

        // ---------------------------------------------------------------- METANOIA -> Google

        // Đẩy slot đã duyệt và đang làm (Approved, Due, InProgress, Overrun) trong khoảng ngày lên Google.
        // Slot đã đẩy mà sau đó Cancelled, Skipped hoặc Postponed thì xóa sự kiện tương ứng. Mỗi slot lưu riêng, một slot lỗi không chặn các slot còn lại
        private async Task PushSlotsAsync(GoogleCalendarSession session, SyncRange? range, GoogleCalendarSyncResultDto result, CancellationToken cancellationToken)
        {
            var slots = await _slotRepository.GetForGooglePushAsync(
                session.Connection.UserId, range?.FromUtc, range?.ToUtc, LiveSlotStatuses, RemovedSlotStatuses, cancellationToken);

            foreach (var slot in slots)
            {
                try
                {
                    if (LiveSlotStatuses.Contains(slot.Status))
                    {
                        await UpsertSlotEventAsync(session, slot, result, cancellationToken);
                    }
                    else
                    {
                        await RemoveSlotEventAsync(session, slot, result, cancellationToken);
                    }

                    slot.SyncFailed = false;
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
                catch (GoogleApiException ex) when (ex.HttpStatusCode != HttpStatusCode.Unauthorized)
                {
                    // 401 là kết nối bị thu hồi: để lỗi đi lên xử lý chung. Lỗi khác chỉ đánh dấu slot này
                    _logger.LogError(ex, "Không đẩy được slot {SlotId} lên Google Calendar ({StatusCode}).", slot.Id, (int)ex.HttpStatusCode);
                    result.PushFailed++;
                    if (result.PushErrors.Count < MaxPushErrors)
                    {
                        result.PushErrors.Add($"Slot {slot.Id}: Google trả lỗi {(int)ex.HttpStatusCode}.");
                    }

                    slot.SyncFailed = true;
                    await TrySaveAsync(cancellationToken);
                }
            }

            session.Connection.LastSyncedAt = DateTime.UtcNow;
            await TrySaveAsync(cancellationToken);
        }

        private async Task UpsertSlotEventAsync(GoogleCalendarSession session, ScheduleSlot slot, GoogleCalendarSyncResultDto result, CancellationToken cancellationToken)
        {
            var service = session.Service;
            var hadEvent = !string.IsNullOrEmpty(slot.GoogleEventId);
            var eventId = slot.GoogleEventId ?? GoogleEventIds.ForSlot(slot.Id);

            Event? saved = null;
            var created = !hadEvent;

            if (hadEvent)
            {
                try
                {
                    saved = await service.Events.Update(ToGoogleEvent(slot, eventId), CalendarId, eventId).ExecuteAsync(cancellationToken);
                }
                catch (GoogleApiException ex) when (ex.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    // Người dùng đã xóa sự kiện trên Google: tạo lại
                    created = true;
                }
            }

            if (saved is null)
            {
                try
                {
                    saved = await service.Events.Insert(ToGoogleEvent(slot, eventId), CalendarId).ExecuteAsync(cancellationToken);
                }
                catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Conflict)
                {
                    // Id này đã có (từ lần đẩy trước chưa kịp lưu, hoặc sự kiện đã bị xóa): ghi đè và khôi phục
                    saved = await service.Events.Update(ToGoogleEvent(slot, eventId), CalendarId, eventId).ExecuteAsync(cancellationToken);
                }
            }

            if (created) result.PushedCreated++;
            else result.PushedUpdated++;

            slot.GoogleEventId = saved.Id;

            // Ghi sự kiện vào bảng cache với cờ "do METANOIA tạo" để lịch gộp không hiện trùng với chính slot này
            var connection = session.Connection;
            var local = await _eventRepository.GetByGoogleEventIdAsync(connection.Id, saved.Id, cancellationToken);
            if (local is null)
            {
                local = new GoogleCalendarEvent
                {
                    Id = Guid.NewGuid(),
                    GoogleConnectionId = connection.Id,
                    GoogleEventId = saved.Id
                };
                await _eventRepository.AddAsync(local, cancellationToken);
            }

            local.IsCreatedByMetanoia = true;
            GoogleEventMapping.Apply(local, saved);
        }

        private async Task RemoveSlotEventAsync(GoogleCalendarSession session, ScheduleSlot slot, GoogleCalendarSyncResultDto result, CancellationToken cancellationToken)
        {
            var eventId = slot.GoogleEventId;
            if (string.IsNullOrEmpty(eventId))
            {
                return;
            }

            try
            {
                await session.Service.Events.Delete(CalendarId, eventId).ExecuteAsync(cancellationToken);
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                // Đã bị xóa từ trước: coi như xong
            }

            var local = await _eventRepository.GetByGoogleEventIdAsync(session.Connection.Id, eventId, cancellationToken);
            if (local is not null && !local.IsCancelled)
            {
                local.IsCancelled = true;
                local.UpdatedAt = DateTime.UtcNow;
            }

            slot.GoogleEventId = null;
            result.PushedDeleted++;
        }

        private static Event ToGoogleEvent(ScheduleSlot slot, string eventId)
        {
            var title = slot.SubTask is null ? slot.Task.Title : $"{slot.Task.Title} - {slot.SubTask.Title}";

            return new Event
            {
                Id = eventId,
                Status = "confirmed",
                Summary = title,
                Description = $"Dự án: {slot.Task.Project.Name}\nTạo bởi METANOIA.",
                Start = new EventDateTime { DateTimeDateTimeOffset = AsUtc(slot.StartTime) },
                End = new EventDateTime { DateTimeDateTimeOffset = AsUtc(slot.EndTime) },
                ExtendedProperties = new Event.ExtendedPropertiesData
                {
                    Private__ = new Dictionary<string, string> { ["metanoiaSlotId"] = slot.Id.ToString() }
                }
            };
        }

        private static DateTimeOffset AsUtc(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }
    }
}
