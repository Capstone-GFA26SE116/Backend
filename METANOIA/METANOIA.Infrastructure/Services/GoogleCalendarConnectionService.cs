using System.Security.Cryptography;
using System.Text;
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
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

        public GoogleCalendarConnectionService(
            IGoogleConnectionRepository connectionRepository,
            IUnitOfWork unitOfWork,
            GoogleOAuthClient oauthClient,
            IDataProtectionProvider dataProtectionProvider,
            IConfiguration configuration)
        {
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
            // Cột timestamp không có time zone: Npgsql từ chối DateTime có Kind = Utc
            connection.TokenExpiresAt = DateTime.SpecifyKind(
                DateTime.UtcNow.AddSeconds(tokens.ExpiresIn), DateTimeKind.Unspecified);
            connection.Status = "Active";
            connection.LastSyncError = null;

            await _unitOfWork.SaveChangesAsync(cancellationToken);
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
