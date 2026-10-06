using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public sealed record GoogleCalendarSession(GoogleConnection Connection, CalendarService Service);

    public class GoogleCalendarClientProvider
    {
        private readonly IGoogleConnectionRepository _connectionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly GoogleOAuthClient _oauthClient;
        private readonly IDataProtector _tokenProtector;

        public GoogleCalendarClientProvider(
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

        public async Task<GoogleCalendarSession> OpenAsync(Guid userId, CancellationToken cancellationToken)
        {
            var connection = await _connectionRepository.GetByUserIdAsync(userId, cancellationToken);
            if (connection is null || connection.Status != "Active")
            {
                throw new GoogleCalendarNotConnectedException();
            }

            var accessToken = await GetValidAccessTokenAsync(connection, cancellationToken);
            var service = new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
                ApplicationName = "METANOIA"
            });

            return new GoogleCalendarSession(connection, service);
        }

        private async Task<string> GetValidAccessTokenAsync(GoogleConnection connection, CancellationToken cancellationToken)
        {
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
            connection.TokenExpiresAt = DateTime.UtcNow.AddSeconds(refreshed.ExpiresIn);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return refreshed.AccessToken;
        }
    }
}
