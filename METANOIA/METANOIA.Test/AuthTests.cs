using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Test.Infrastructure;
using Xunit;

namespace METANOIA.Test
{
    public class AuthTests
    {
        [Fact]
        public async Task Login_WithValidGoogleToken_ReturnsAccessTokenAndFreelancerRole()
        {
            using var factory = new ApiFactory();
            var (_, auth) = await TestHelpers.LoginAsync(factory, "alice");

            Assert.False(string.IsNullOrEmpty(auth.AccessToken));
            Assert.Equal("Freelancer", auth.Role);
            Assert.Equal("alice@test.com", auth.Email);
        }

        [Fact]
        public async Task Login_WithInvalidGoogleToken_Returns400()
        {
            using var factory = new ApiFactory();
            await factory.EnsureDefaultRoleAsync();
            var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/auth/google", new GoogleLoginRequestDto { IdToken = "garbage" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Login_TwiceWithSameGoogleAccount_ReturnsSameUser()
        {
            using var factory = new ApiFactory();
            var (_, first) = await TestHelpers.LoginAsync(factory, "bob");
            var (_, second) = await TestHelpers.LoginAsync(factory, "bob");

            Assert.Equal(first.UserId, second.UserId);
        }
    }
}
