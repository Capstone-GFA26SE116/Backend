using System.Net.Http.Json;
using System.Text.Json.Serialization;
using METANOIA.Application.Exceptions;
using Microsoft.Extensions.Configuration;

namespace METANOIA.Infrastructure.Services
{
    public sealed record GoogleTokenResult(string AccessToken, string? RefreshToken, int ExpiresIn);

    public class GoogleOAuthClient
    {
        private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _redirectUri;

        public GoogleOAuthClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _clientId = configuration["Google:ClientId"]
                ?? throw new InvalidOperationException("Cấu hình 'Google:ClientId' chưa được thiết lập.");
            _clientSecret = configuration["Google:ClientSecret"]
                ?? throw new InvalidOperationException("Cấu hình 'Google:ClientSecret' chưa được thiết lập.");
            _redirectUri = configuration["Google:CalendarRedirectUri"]
                ?? throw new InvalidOperationException("Cấu hình 'Google:CalendarRedirectUri' chưa được thiết lập.");
        }

        public async Task<GoogleTokenResult> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
        {
            var payload = await PostTokenRequestAsync(new Dictionary<string, string>
            {
                ["code"] = code,
                ["redirect_uri"] = _redirectUri,
                ["grant_type"] = "authorization_code"
            }, cancellationToken);

            if (payload?.AccessToken is null || payload.RefreshToken is null)
            {
                throw new UserFriendlyException("Không thể xác thực với Google Calendar, vui lòng thử kết nối lại.");
            }

            return new GoogleTokenResult(payload.AccessToken, payload.RefreshToken, payload.ExpiresIn);
        }

        // Trả về null khi Google từ chối refresh token (thường là user đã thu hồi quyền).
        public async Task<GoogleTokenResult?> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
        {
            var payload = await PostTokenRequestAsync(new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            }, cancellationToken);

            if (payload?.AccessToken is null)
            {
                return null;
            }

            return new GoogleTokenResult(payload.AccessToken, null, payload.ExpiresIn);
        }

        private async Task<GoogleTokenPayload?> PostTokenRequestAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
        {
            form["client_id"] = _clientId;
            form["client_secret"] = _clientSecret;

            var client = _httpClientFactory.CreateClient();
            using var content = new FormUrlEncodedContent(form);
            using var response = await client.PostAsync(TokenEndpoint, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<GoogleTokenPayload>(cancellationToken);
        }

        private sealed class GoogleTokenPayload
        {
            [JsonPropertyName("access_token")]
            public string? AccessToken { get; set; }

            [JsonPropertyName("refresh_token")]
            public string? RefreshToken { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }
    }
}
