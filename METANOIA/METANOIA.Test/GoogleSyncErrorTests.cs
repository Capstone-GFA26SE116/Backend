using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using METANOIA.Test.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // POST /api/google-calendar/sync: mỗi loại lỗi có mã trả về và thông báo riêng, và luôn được LogError
    public class GoogleSyncErrorTests
    {
        private const string Sync = "/api/google-calendar/sync";

        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        // Token hợp lệ về mặt mã hóa (cùng khóa DataProtection với ứng dụng) hoặc rác không giải mã được
        private static async Task SeedConnectionAsync(
            ApiFactory factory, Guid userId, string status = "Active", bool undecryptable = false, bool accessTokenExpired = false)
        {
            var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("GoogleCalendar.Tokens");
            byte[] Protect(string value) => undecryptable ? new byte[] { 1, 2, 3 } : protector.Protect(Encoding.UTF8.GetBytes(value));

            await factory.SeedAsync(db => db.GoogleConnections.Add(new GoogleConnection
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AccessTokenEncrypted = Protect("access-token"),
                RefreshTokenEncrypted = Protect("refresh-token"),
                TokenExpiresAt = accessTokenExpired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(1),
                Status = status,
                CreatedAt = DateTime.UtcNow
            }));
        }

        private static async Task<(string Message, string? Code)> ReadErrorAsync(HttpResponseMessage response)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            return (root.GetProperty("message").GetString()!, root.TryGetProperty("code", out var code) ? code.GetString() : null);
        }

        private static IEnumerable<CapturedLog> Errors(ApiFactory factory) =>
            factory.Logs.Entries.Where(e => e.Level == LogLevel.Error && e.Category.Contains("GoogleCalendarSyncService"));

        // ------------------------------------------------------------ Chưa kết nối

        [Fact]
        public async Task NoConnection_Returns400_WithAClearMessage_AndLogsAnError()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");

            var response = await client.PostAsync(Sync, null);
            var (message, _) = await ReadErrorAsync(response);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Chưa kết nối Google Calendar.", message);
            Assert.NotEmpty(Errors(factory));
        }

        // ------------------------------------------------------------ Expired / Revoked

        [Theory]
        [InlineData("Expired")]
        [InlineData("Revoked")]
        public async Task ExpiredOrRevokedConnection_Returns409_AskingToReconnect_AndKeepsItsStatus(string status)
        {
            using var factory = new ApiFactory();
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            await SeedConnectionAsync(factory, auth.UserId, status);

            var response = await client.PostAsync(Sync, null);
            var (message, code) = await ReadErrorAsync(response);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("Cần kết nối lại Google Calendar.", message);
            Assert.Equal("GOOGLE_RECONNECT_REQUIRED", code);
            Assert.Contains("/api/google-calendar/connect", await response.Content.ReadAsStringAsync());
            Assert.Equal(status, (await QueryAsync(factory, db => db.GoogleConnections.SingleAsync())).Status);
            Assert.NotEmpty(Errors(factory));
            Assert.Empty(factory.GoogleHttp.Requests); // không gọi Google khi đã biết cần kết nối lại
        }

        // ------------------------------------------------------------ Không giải mã được token

        [Fact]
        public async Task UndecryptableTokens_Return409_WithoutLeakingDetails_LogTheError_AndDoNotRevokeTheConnection()
        {
            using var factory = new ApiFactory();
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            await SeedConnectionAsync(factory, auth.UserId, undecryptable: true);

            var response = await client.PostAsync(Sync, null);
            var body = await response.Content.ReadAsStringAsync();
            var (message, code) = await ReadErrorAsync(response);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("Cần kết nối lại Google Calendar.", message);
            Assert.Equal("GOOGLE_RECONNECT_REQUIRED", code);
            Assert.DoesNotContain("Cryptograph", body);
            Assert.DoesNotContain("DataProtection", body);
            Assert.DoesNotContain("at METANOIA", body);
            // Khóa giải mã khác môi trường không có nghĩa là Google đã thu hồi quyền: giữ nguyên Active
            Assert.Equal("Active", (await QueryAsync(factory, db => db.GoogleConnections.SingleAsync())).Status);
            var logged = Assert.Single(Errors(factory), e => e.Exception is System.Security.Cryptography.CryptographicException);
            Assert.Contains(auth.UserId.ToString(), logged.Message);
        }

        // ------------------------------------------------------------ invalid_grant

        [Fact]
        public async Task InvalidGrant_MarksTheConnectionRevoked_AndReturns409()
        {
            using var factory = new ApiFactory();
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            await SeedConnectionAsync(factory, auth.UserId, accessTokenExpired: true);
            factory.GoogleHttp.Responder = _ => FakeGoogleHttpHandler.Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"error_description\":\"Token has been expired or revoked.\"}");

            var response = await client.PostAsync(Sync, null);
            var (message, code) = await ReadErrorAsync(response);
            var connection = await QueryAsync(factory, db => db.GoogleConnections.SingleAsync());

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("Cần kết nối lại Google Calendar.", message);
            Assert.Equal("GOOGLE_RECONNECT_REQUIRED", code);
            Assert.Equal("Revoked", connection.Status);
            Assert.NotEmpty(Errors(factory));
            Assert.Contains(factory.GoogleHttp.Requests, r => r.Contains("oauth2.googleapis.com/token"));

            // Lần sau không cần hỏi Google nữa
            var again = await client.PostAsync(Sync, null);
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Single(factory.GoogleHttp.Requests);
        }

        // ------------------------------------------------------------ Google lỗi

        [Theory]
        [InlineData(HttpStatusCode.ServiceUnavailable, "")]
        [InlineData(HttpStatusCode.InternalServerError, "<html>oops</html>")]
        [InlineData(HttpStatusCode.BadRequest, "{\"error\":\"invalid_client\"}")]
        public async Task GoogleTokenServerFailures_Return502_RecordLastSyncError_AndDoNotRevoke(HttpStatusCode status, string body)
        {
            using var factory = new ApiFactory();
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            await SeedConnectionAsync(factory, auth.UserId, accessTokenExpired: true);
            factory.GoogleHttp.Responder = _ => FakeGoogleHttpHandler.Json(status, body);

            var response = await client.PostAsync(Sync, null);
            var (message, code) = await ReadErrorAsync(response);
            var connection = await QueryAsync(factory, db => db.GoogleConnections.SingleAsync());

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal("GOOGLE_API_ERROR", code);
            Assert.DoesNotContain(((int)status).ToString(), message); // phản hồi cho người dùng không lộ chi tiết
            Assert.Equal("Active", connection.Status);
            Assert.Contains(((int)status).ToString(), connection.LastSyncError);
            Assert.NotEmpty(Errors(factory));
        }

        [Fact]
        public async Task NetworkFailureTalkingToGoogle_Returns502_AndRecordsLastSyncError()
        {
            using var factory = new ApiFactory();
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            await SeedConnectionAsync(factory, auth.UserId, accessTokenExpired: true);
            factory.GoogleHttp.Responder = _ => throw new HttpRequestException("Mất mạng giả lập");

            var response = await client.PostAsync(Sync, null);
            var (_, code) = await ReadErrorAsync(response);
            var connection = await QueryAsync(factory, db => db.GoogleConnections.SingleAsync());

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal("GOOGLE_API_ERROR", code);
            Assert.Equal("Active", connection.Status);
            Assert.Contains("Mất mạng giả lập", connection.LastSyncError);
            Assert.Contains(Errors(factory), e => e.Exception is HttpRequestException);
        }

        // ------------------------------------------------------------ Không ảnh hưởng chỗ khác

        [Fact]
        public async Task EventsEndpoint_KeepsItsOwn409ForAMissingConnection()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");

            var response = await client.GetAsync("/api/google-calendar/events?from=2026-10-01T00:00:00Z&to=2026-10-31T00:00:00Z");
            var (_, code) = await ReadErrorAsync(response);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("GOOGLE_CALENDAR_NOT_CONNECTED", code);
        }

        [Fact]
        public async Task Sync_RequiresLogin()
        {
            using var factory = new ApiFactory();

            var response = await factory.CreateClient().PostAsync(Sync, null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
