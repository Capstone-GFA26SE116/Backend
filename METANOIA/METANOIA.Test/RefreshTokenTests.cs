using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using METANOIA.Test.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    public class RefreshTokenTests
    {
        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static Task<HttpResponseMessage> RefreshAsync(ApiFactory factory, string token)
        {
            return factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new RefreshRequestDto { RefreshToken = token });
        }

        private static Task<HttpResponseMessage> LogoutAsync(ApiFactory factory, string token, bool allDevices = false)
        {
            return factory.CreateClient().PostAsJsonAsync("/api/auth/logout", new LogoutRequestDto { RefreshToken = token, AllDevices = allDevices });
        }

        private static async Task<AuthResponseDto> LoginAsync(ApiFactory factory, string subject = "user-1")
        {
            var (_, auth) = await TestHelpers.LoginAsync(factory, subject);
            return auth;
        }

        private static async Task<int> ActiveTokensAsync(ApiFactory factory, Guid userId)
        {
            return await QueryAsync(factory, db => db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAt == null));
        }

        [Fact]
        public async Task Login_ReturnsRefreshToken_AndStoresOnlyItsHash()
        {
            using var factory = new ApiFactory();

            var auth = await LoginAsync(factory);
            var stored = Assert.Single(await QueryAsync(factory, db => db.RefreshTokens.ToListAsync()));

            Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
            Assert.True(auth.RefreshTokenExpiresAtUtc > DateTime.UtcNow.AddDays(29));
            Assert.Equal(64, stored.TokenHash.Length);
            Assert.NotEqual(auth.RefreshToken, stored.TokenHash);
            Assert.DoesNotContain(auth.RefreshToken, stored.TokenHash);
            Assert.Null(stored.RevokedAt);
        }

        [Fact]
        public async Task Refresh_ReturnsWorkingAccessToken_AndRotatesTheRefreshToken()
        {
            using var factory = new ApiFactory();
            var auth = await LoginAsync(factory);

            var response = await RefreshAsync(factory, auth.RefreshToken);
            var renewed = await response.Content.ReadFromJsonAsync<AuthResponseDto>();

            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", renewed!.AccessToken);
            var me = await client.GetAsync("/api/me");
            var tokens = await QueryAsync(factory, db => db.RefreshTokens.OrderBy(t => t.CreatedAt).ToListAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(auth.UserId, renewed.UserId);
            Assert.NotEqual(auth.RefreshToken, renewed.RefreshToken);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            Assert.Equal(2, tokens.Count);
            Assert.NotNull(tokens[0].RevokedAt);
            Assert.Equal(tokens[1].Id, tokens[0].ReplacedById);
            Assert.Null(tokens[1].RevokedAt);
        }

        [Fact]
        public async Task Refresh_NewTokenCanBeRefreshedAgain()
        {
            using var factory = new ApiFactory();
            var auth = await LoginAsync(factory);

            var first = await (await RefreshAsync(factory, auth.RefreshToken)).Content.ReadFromJsonAsync<AuthResponseDto>();
            var second = await RefreshAsync(factory, first!.RefreshToken);

            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal(1, await ActiveTokensAsync(factory, auth.UserId));
        }

        [Theory]
        [InlineData("not-a-real-token")]
        [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        public async Task Refresh_WithUnknownToken_Returns401(string token)
        {
            using var factory = new ApiFactory();
            await LoginAsync(factory);

            var response = await RefreshAsync(factory, token);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("INVALID_REFRESH_TOKEN", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Refresh_WithoutToken_Returns400()
        {
            using var factory = new ApiFactory();

            var response = await RefreshAsync(factory, "  ");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Refresh_WithExpiredToken_Returns401()
        {
            using var factory = new ApiFactory();
            var auth = await LoginAsync(factory);
            await factory.SeedAsync(db =>
            {
                var token = db.RefreshTokens.Single();
                token.CreatedAt = DateTime.UtcNow.AddDays(-31);
                token.ExpiresAt = DateTime.UtcNow.AddDays(-1);
            });

            var response = await RefreshAsync(factory, auth.RefreshToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task OldTokenUsedAgainRightAway_IsRejected_ButTheNewSessionSurvives()
        {
            using var factory = new ApiFactory();
            var auth = await LoginAsync(factory);
            var renewed = await (await RefreshAsync(factory, auth.RefreshToken)).Content.ReadFromJsonAsync<AuthResponseDto>();

            // Hai tab cùng gửi một refresh token: tab đến sau bị từ chối nhưng không làm mất phiên mới
            var duplicate = await RefreshAsync(factory, auth.RefreshToken);
            var stillWorks = await RefreshAsync(factory, renewed!.RefreshToken);

            Assert.Equal(HttpStatusCode.Unauthorized, duplicate.StatusCode);
            Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
        }

        [Fact]
        public async Task OldTokenReusedLater_IsTreatedAsTheft_AndRevokesEverySession()
        {
            using var factory = new ApiFactory();
            var auth = await LoginAsync(factory);
            var renewed = await (await RefreshAsync(factory, auth.RefreshToken)).Content.ReadFromJsonAsync<AuthResponseDto>();
            await factory.SeedAsync(db =>
            {
                var rotated = db.RefreshTokens.Single(t => t.RevokedAt != null);
                rotated.RevokedAt = DateTime.UtcNow.AddMinutes(-5);
            });

            var replay = await RefreshAsync(factory, auth.RefreshToken);
            var legitimateUser = await RefreshAsync(factory, renewed!.RefreshToken);

            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, legitimateUser.StatusCode);
            Assert.Equal(0, await ActiveTokensAsync(factory, auth.UserId));
        }

        [Fact]
        public async Task Logout_RevokesTheToken_SoItCannotBeRefreshed()
        {
            using var factory = new ApiFactory();
            var auth = await LoginAsync(factory);

            var logout = await LogoutAsync(factory, auth.RefreshToken);
            var refresh = await RefreshAsync(factory, auth.RefreshToken);

            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
            Assert.Equal(0, await ActiveTokensAsync(factory, auth.UserId));
        }

        [Fact]
        public async Task Logout_OnlyEndsThatDevice_UnlessAllDevicesIsRequested()
        {
            using var factory = new ApiFactory();
            var phone = await LoginAsync(factory);
            var laptop = await LoginAsync(factory);

            await LogoutAsync(factory, phone.RefreshToken);
            var laptopStillWorks = await RefreshAsync(factory, laptop.RefreshToken);
            var renewedLaptop = await laptopStillWorks.Content.ReadFromJsonAsync<AuthResponseDto>();

            await LogoutAsync(factory, renewedLaptop!.RefreshToken, allDevices: true);

            Assert.Equal(HttpStatusCode.OK, laptopStillWorks.StatusCode);
            Assert.Equal(0, await ActiveTokensAsync(factory, phone.UserId));
        }

        [Fact]
        public async Task LogoutAllDevices_RevokesTheOtherSessionsToo()
        {
            using var factory = new ApiFactory();
            var phone = await LoginAsync(factory);
            var laptop = await LoginAsync(factory);

            var response = await LogoutAsync(factory, phone.RefreshToken, allDevices: true);
            var laptopRefresh = await RefreshAsync(factory, laptop.RefreshToken);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, laptopRefresh.StatusCode);
        }

        [Fact]
        public async Task Logout_IsIdempotent_AndSilentAboutUnknownTokens()
        {
            using var factory = new ApiFactory();
            var auth = await LoginAsync(factory);

            var first = await LogoutAsync(factory, auth.RefreshToken);
            var again = await LogoutAsync(factory, auth.RefreshToken);
            var unknown = await LogoutAsync(factory, "never-issued");

            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
        }

        [Fact]
        public async Task DeletedAccount_CannotRefresh_AndItsSessionsAreRevoked()
        {
            using var factory = new ApiFactory();
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");

            await client.DeleteAsync("/api/me");
            var refresh = await RefreshAsync(factory, auth.RefreshToken);

            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
            Assert.Equal(0, await ActiveTokensAsync(factory, auth.UserId));
        }

        [Fact]
        public async Task EachLogin_GetsItsOwnIndependentToken()
        {
            using var factory = new ApiFactory();

            var first = await LoginAsync(factory);
            var second = await LoginAsync(factory);

            Assert.NotEqual(first.RefreshToken, second.RefreshToken);
            Assert.Equal(2, await ActiveTokensAsync(factory, first.UserId));
        }
    }
}
