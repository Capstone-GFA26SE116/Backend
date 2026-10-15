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
        private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

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

        // Trả về null CHỈ khi Google báo invalid_grant (refresh token bị thu hồi hoặc hết hiệu lực).
        // Mọi lỗi khác (Google lỗi 5xx, sai client secret, mạng) ném GoogleApiFailureException chứ không coi là bị thu hồi quyền.
        public async Task<GoogleTokenResult?> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
        {
            var (payload, error, status) = await PostTokenRequestDetailedAsync(new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            }, cancellationToken);

            if (payload?.AccessToken is not null)
            {
                return new GoogleTokenResult(payload.AccessToken, null, payload.ExpiresIn);
            }

            if (string.Equals(error, "invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            throw new GoogleApiFailureException(
                $"Google từ chối làm mới access token (HTTP {status}{(error is null ? string.Empty : $", {error}")}).");
        }

        // Thu hồi quyền đã cấp cho METANOIA. Chỉ cố gắng hết sức: trả false nếu Google từ chối (ví dụ token đã bị thu hồi)
        public async Task<bool> RevokeAsync(string token, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token });
            using var response = await client.PostAsync(RevokeEndpoint, content, cancellationToken);
            return response.IsSuccessStatusCode;
        }

        private async Task<GoogleTokenPayload?> PostTokenRequestAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
        {
            return (await PostTokenRequestDetailedAsync(form, cancellationToken)).Payload;
        }

        // Trả cả mã lỗi OAuth ("error" trong thân phản hồi, ví dụ invalid_grant) để phân biệt bị thu hồi quyền với Google lỗi
        private async Task<(GoogleTokenPayload? Payload, string? Error, int Status)> PostTokenRequestDetailedAsync(
            Dictionary<string, string> form, CancellationToken cancellationToken)
        {
            form["client_id"] = _clientId;
            form["client_secret"] = _clientSecret;

            var client = _httpClientFactory.CreateClient();
            using var content = new FormUrlEncodedContent(form);
            using var response = await client.PostAsync(TokenEndpoint, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return (await response.Content.ReadFromJsonAsync<GoogleTokenPayload>(cancellationToken), null, (int)response.StatusCode);
            }

            string? error = null;
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (document.RootElement.TryGetProperty("error", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    error = value.GetString();
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Thân phản hồi không phải JSON (ví dụ trang lỗi 5xx): coi như không có mã lỗi OAuth
            }

            return (null, error, (int)response.StatusCode);
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
