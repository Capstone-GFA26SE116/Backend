using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Infrastructure.Data;
using METANOIA.Test.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // PUT /api/me/profile: người dùng tự sửa hồ sơ của mình
    public class ProfileTests
    {
        [Fact]
        public async Task UpdateProfile_ChangesTheFullName_AndTrimsIt()
        {
            using var factory = new ApiFactory();
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");

            var response = await client.PutAsJsonAsync("/api/me/profile", new UpdateProfileRequestDto { FullName = "  Nguyễn Văn A  " });
            var me = await response.Content.ReadFromJsonAsync<MeDto>();
            var reloaded = await client.GetFromJsonAsync<MeDto>("/api/me");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Nguyễn Văn A", me!.FullName);
            Assert.Equal("Nguyễn Văn A", reloaded!.FullName);
            Assert.Equal(auth.UserId, me.Id);
        }

        [Fact]
        public async Task UpdateProfile_LeavesEverythingElseUntouched()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");
            var before = await client.GetFromJsonAsync<MeDto>("/api/me");

            await client.PutAsJsonAsync("/api/me/profile", new UpdateProfileRequestDto { FullName = "Tên mới" });
            var after = await client.GetFromJsonAsync<MeDto>("/api/me");

            Assert.Equal(before!.Email, after!.Email);
            Assert.Equal(before.Role, after.Role);
            Assert.Equal(before.TimeZone, after.TimeZone);
            Assert.Equal(before.WorkDayStart, after.WorkDayStart);
            Assert.Equal(before.WorkDayEnd, after.WorkDayEnd);
            Assert.Equal(before.OnboardingCompleted, after.OnboardingCompleted);
            Assert.Equal(before.CurrentSubscription!.PlanName, after.CurrentSubscription!.PlanName);
        }

        [Fact]
        public async Task UpdateProfile_CannotBeUsedToChangeRoleOrEmail_EvenIfSentInTheBody()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");

            var response = await client.PutAsJsonAsync("/api/me/profile", new
            {
                fullName = "Tên mới",
                role = "Admin",
                email = "hacker@evil.com"
            });
            var me = await client.GetFromJsonAsync<MeDto>("/api/me");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Freelancer", me!.Role);
            Assert.Equal("user-1@test.com", me.Email);
        }

        [Fact]
        public async Task UpdateProfile_OnlyAffectsTheCaller()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");
            var (other, otherAuth) = await TestHelpers.LoginAsync(factory, "user-2");

            await client.PutAsJsonAsync("/api/me/profile", new UpdateProfileRequestDto { FullName = "Đã đổi" });

            var otherName = (await other.GetFromJsonAsync<MeDto>("/api/me"))!.FullName;
            Assert.Equal("Test user-2", otherName);
            using var scope = factory.Services.CreateScope();
            var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Id == otherAuth.UserId);
            Assert.Equal("Test user-2", stored.FullName);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task UpdateProfile_RejectsBlankName(string name)
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");

            var response = await client.PutAsJsonAsync("/api/me/profile", new UpdateProfileRequestDto { FullName = name });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Test user-1", (await client.GetFromJsonAsync<MeDto>("/api/me"))!.FullName);
        }

        [Fact]
        public async Task UpdateProfile_RejectsTooLongNames_AndAcceptsExactlyTheLimit()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");

            var tooLong = await client.PutAsJsonAsync("/api/me/profile", new UpdateProfileRequestDto { FullName = new string('a', 101) });
            var limit = await client.PutAsJsonAsync("/api/me/profile", new UpdateProfileRequestDto { FullName = new string('a', 100) });

            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
            Assert.Equal(HttpStatusCode.OK, limit.StatusCode);
        }

        [Fact]
        public async Task UpdateProfile_WithMissingBody_Returns400_AndRequiresLogin()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");

            var noName = await client.PutAsJsonAsync("/api/me/profile", new { });
            var anonymous = await factory.CreateClient().PutAsJsonAsync("/api/me/profile", new UpdateProfileRequestDto { FullName = "A" });

            Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
    }
}
