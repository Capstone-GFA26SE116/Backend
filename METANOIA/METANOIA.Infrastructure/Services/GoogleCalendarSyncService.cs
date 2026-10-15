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

        private readonly GoogleCalendarClientProvider _provider;
        private readonly IGoogleConnectionRepository _connectionRepository;
        private readonly IGoogleCalendarEventRepository _eventRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<GoogleCalendarSyncService> _logger;

        public GoogleCalendarSyncService(
            GoogleCalendarClientProvider provider,
            IGoogleConnectionRepository connectionRepository,
            IGoogleCalendarEventRepository eventRepository,
            IUnitOfWork unitOfWork,
            ILogger<GoogleCalendarSyncService> logger)
        {
            _provider = provider;
            _connectionRepository = connectionRepository;
            _eventRepository = eventRepository;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        // Kết quả cho người gọi:
        //   chưa có GoogleConnection            -> 400 "Chưa kết nối Google Calendar"
        //   Expired/Revoked, invalid_grant,
        //   không giải mã được token đã lưu     -> 409 "Cần kết nối lại Google Calendar" (không lộ chi tiết)
        //   Google lỗi / quá hạn / mất mạng     -> ghi LastSyncError, 502
        // Mọi exception đều được LogError trước khi trả về.
        public async Task<GoogleCalendarSyncResultDto> SyncAsync(Guid userId, CancellationToken cancellationToken = default)
        {
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

            try
            {
                var session = await _provider.OpenAsync(userId, cancellationToken);

                try
                {
                    return await RunSyncAsync(session, cancellationToken);
                }
                catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Gone)
                {
                    // syncToken đã hết hạn: Google yêu cầu đồng bộ lại từ đầu
                    _logger.LogError(ex, "syncToken của user {UserId} đã hết hạn, đồng bộ lại từ đầu.", userId);
                    session.Connection.SyncToken = null;
                    return await RunSyncAsync(session, cancellationToken);
                }
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

        private async Task<GoogleCalendarSyncResultDto> RunSyncAsync(GoogleCalendarSession session, CancellationToken cancellationToken)
        {
            var connection = session.Connection;
            var result = new GoogleCalendarSyncResultDto();
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

            return result;
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
                    IsCreatedByMetanoia = false
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
    }
}
