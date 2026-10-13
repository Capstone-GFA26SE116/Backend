using System.Security.Cryptography;
using System.Text;
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class GoogleCalendarConnectionService : IGoogleCalendarConnectionService
    {
        private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string CalendarEventsScope = "https://www.googleapis.com/auth/calendar.events";

        private readonly IGoogleConnectionRepository _connectionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly GoogleOAuthClient _oauthClient;
        private readonly ITimeLimitedDataProtector _stateProtector;
        private readonly IDataProtector _tokenProtector;
        private readonly string _clientId;
        private readonly string _redirectUri;
        private readonly GoogleCalendarClientProvider _clientProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GoogleCalendarConnectionService> _logger;

        public GoogleCalendarConnectionService(
            IGoogleConnectionRepository connectionRepository,
            IUnitOfWork unitOfWork,
            GoogleOAuthClient oauthClient,
            IDataProtectionProvider dataProtectionProvider,
            IConfiguration configuration,
            GoogleCalendarClientProvider clientProvider,
            ILogger<GoogleCalendarConnectionService> logger)
        {
            _clientProvider = clientProvider;
            _configuration = configuration;
            _logger = logger;
            _connectionRepository = connectionRepository;
            _unitOfWork = unitOfWork;
            _oauthClient = oauthClient;
            _stateProtector = dataProtectionProvider
                .CreateProtector("GoogleCalendar.State")
                .ToTimeLimitedDataProtector();
            _tokenProtector = dataProtectionProvider.CreateProtector("GoogleCalendar.Tokens");
            _clientId = configuration["Google:ClientId"]
                ?? throw new InvalidOperationException("Cấu hình 'Google:ClientId' chưa được thiết lập.");
            _redirectUri = configuration["Google:CalendarRedirectUri"]
                ?? throw new InvalidOperationException("Cấu hình 'Google:CalendarRedirectUri' chưa được thiết lập.");
        }

        public string BuildAuthorizationUrl(Guid userId)
        {
            var state = _stateProtector.Protect(userId.ToString(), DateTimeOffset.UtcNow.AddMinutes(10));

            return $"{AuthorizationEndpoint}" +
                   $"?client_id={Uri.EscapeDataString(_clientId)}" +
                   $"&redirect_uri={Uri.EscapeDataString(_redirectUri)}" +
                   $"&response_type=code" +
                   $"&scope={Uri.EscapeDataString(CalendarEventsScope)}" +
                   $"&access_type=offline" +
                   $"&prompt=consent" +
                   $"&state={Uri.EscapeDataString(state)}";
        }

        public async Task ConnectAsync(string code, string state, CancellationToken cancellationToken = default)
        {
            var userId = ReadUserIdFromState(state);
            var tokens = await _oauthClient.ExchangeCodeAsync(code, cancellationToken);

            var connection = await _connectionRepository.GetByUserIdAsync(userId, cancellationToken);
            if (connection is null)
            {
                connection = new GoogleConnection
                {
                    Id = Guid.NewGuid(),
                    UserId = userId
                };
                await _connectionRepository.AddAsync(connection, cancellationToken);
            }

            connection.AccessTokenEncrypted = _tokenProtector.Protect(Encoding.UTF8.GetBytes(tokens.AccessToken));
            connection.RefreshTokenEncrypted = _tokenProtector.Protect(Encoding.UTF8.GetBytes(tokens.RefreshToken!));
            connection.TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn);
            connection.Status = "Active";
            connection.LastSyncError = null;

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await TryStartWatchAsync(userId, cancellationToken);
        }

        public async Task DisconnectAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var connection = await _connectionRepository.GetWithEventsByUserIdAsync(userId, cancellationToken);
            if (connection is null)
            {
                return;
            }

            string? refreshToken = null;
            try
            {
                refreshToken = Encoding.UTF8.GetString(_tokenProtector.Unprotect(connection.RefreshTokenEncrypted));
            }
            catch (CryptographicException ex)
            {
                _logger.LogWarning(ex, "Không giải mã được refresh token của user {UserId}, bỏ qua bước thu hồi ở Google.", userId);
            }

            // Xóa trước: dù Google có phản hồi hay không thì liên kết trong hệ thống vẫn được hủy
            _connectionRepository.Remove(connection);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (refreshToken is not null)
            {
                try
                {
                    await _oauthClient.RevokeAsync(refreshToken, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Không thu hồi được quyền Google của user {UserId}.", userId);
                }
            }
        }

        // Đăng ký kênh Watch để Google gọi POST /api/google-calendar/webhook khi lịch đổi. Chỉ chạy khi có cấu hình
        // Google:WebhookUrl (URL https công khai). Lỗi ở đây không làm hỏng việc kết nối: lịch vẫn đồng bộ thủ công được.
        private async Task TryStartWatchAsync(Guid userId, CancellationToken cancellationToken)
        {
            var webhookUrl = _configuration["Google:WebhookUrl"];
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                return;
            }

            if (!webhookUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Google:WebhookUrl phải là URL https công khai, bỏ qua việc đăng ký kênh Watch.");
                return;
            }

            try
            {
                var secret = _configuration["Jwt:Key"]
                    ?? throw new InvalidOperationException("Cấu hình 'Jwt:Key' chưa được thiết lập.");
                var session = await _clientProvider.OpenAsync(userId, cancellationToken);
                var channelId = Guid.NewGuid().ToString();

                var channel = await session.Service.Events.Watch(new Channel
                {
                    Id = channelId,
                    Type = "web_hook",
                    Address = webhookUrl,
                    Token = GoogleWebhookToken.Create(channelId, secret)
                }, "primary").ExecuteAsync(cancellationToken);

                session.Connection.WatchChannelId = channelId;
                session.Connection.WatchExpiresAt = channel.Expiration is { } expiration
                    ? DateTimeOffset.FromUnixTimeMilliseconds(expiration).UtcDateTime
                    : null;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Không đăng ký được kênh Watch Google Calendar cho user {UserId}.", userId);
            }
        }

        public async Task<GoogleCalendarStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var connection = await _connectionRepository.GetByUserIdAsync(userId, cancellationToken);
            if (connection is null)
            {
                return new GoogleCalendarStatusDto { IsConnected = false };
            }

            return new GoogleCalendarStatusDto
            {
                IsConnected = connection.Status == "Active",
                Status = connection.Status,
                TokenExpiresAt = connection.TokenExpiresAt
            };
        }

        private Guid ReadUserIdFromState(string state)
        {
            try
            {
                var userIdValue = _stateProtector.Unprotect(state, out _);
                return Guid.Parse(userIdValue);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                throw new UserFriendlyException("Phiên kết nối Google Calendar đã hết hạn hoặc không hợp lệ, vui lòng thử lại.", ex);
            }
        }
    }
}
