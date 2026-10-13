using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using METANOIA.Infrastructure.Services;
using METANOIA.Test.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    public class MeAndInitializationTests
    {
        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static async Task<DomainDto> CreateDomainAsync(HttpClient admin, string name, bool isActive = true)
        {
            var response = await admin.PostAsJsonAsync("/api/domains", new DomainRequestDto { Name = name, IsActive = isActive });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<DomainDto>())!;
        }

        private static OnboardingRequestDto Onboarding(IEnumerable<Guid> domainIds, IEnumerable<int> hours, string level = "Sometimes") => new()
        {
            WorkDayStart = new TimeOnly(9, 0),
            WorkDayEnd = new TimeOnly(17, 0),
            WorksOnWeekend = true,
            DomainIds = domainIds.ToList(),
            ProductiveHours = hours.ToList(),
            OverrunLevel = level
        };

        // ------------------------------------------------------------ Khởi tạo user mới

        [Fact]
        public async Task FirstLogin_CreatesUser_PersonalProject_FreeSubscription_And24CircadianRows()
        {
            using var factory = new ApiFactory();
            var (_, auth) = await TestHelpers.LoginAsync(factory, "user-1");

            var projects = await QueryAsync(factory, db => db.Projects.Where(p => p.UserId == auth.UserId).ToListAsync());
            var subscriptions = await QueryAsync(factory, db => db.UserSubscriptions.Where(s => s.UserId == auth.UserId).ToListAsync());
            var circadian = await QueryAsync(factory, db => db.UserCircadianProfiles.Where(c => c.UserId == auth.UserId).ToListAsync());

            var project = Assert.Single(projects);
            Assert.Equal("Cá nhân", project.Name);
            Assert.True(project.IsDefault);
            Assert.Equal("Active", project.Status);
            Assert.Null(project.ClientId);

            var subscription = Assert.Single(subscriptions);
            Assert.Equal(ApiFactory.FreePlanId, subscription.PlanId);
            Assert.Equal("Active", subscription.Status);
            Assert.Null(subscription.EndAt);
            Assert.Null(subscription.PaymentTransactionId);
            Assert.Equal(0, subscription.AiCallsUsed);

            Assert.Equal(24, circadian.Count);
            Assert.Equal(Enumerable.Range(0, 24), circadian.Select(c => (int)c.HourOfDay).OrderBy(h => h));
            Assert.All(circadian, c =>
            {
                Assert.Equal(0.500m, c.Score);
                Assert.Equal("Onboarding", c.Source);
            });
        }

        [Fact]
        public async Task SecondLogin_DoesNotCreateAnythingExtra()
        {
            using var factory = new ApiFactory();
            await TestHelpers.LoginAsync(factory, "user-1");
            var before = await QueryAsync(factory, async db =>
                (await db.Users.CountAsync(), await db.Projects.CountAsync(), await db.UserSubscriptions.CountAsync(), await db.UserCircadianProfiles.CountAsync()));

            await TestHelpers.LoginAsync(factory, "user-1");
            var after = await QueryAsync(factory, async db =>
                (await db.Users.CountAsync(), await db.Projects.CountAsync(), await db.UserSubscriptions.CountAsync(), await db.UserCircadianProfiles.CountAsync()));

            Assert.Equal((1, 1, 1, 24), before);
            Assert.Equal(before, after);
        }

        [Fact]
        public async Task FailureWhileCreatingProject_LeavesNoUserBehind()
        {
            using var factory = new ApiFactory();
            await factory.EnsureDefaultRoleAsync();
            factory.FailOnProjectCreate = true;

            var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/google", new GoogleLoginRequestDto
            {
                IdToken = "valid:user-1:user-1@test.com:Test user-1"
            });
            var leftovers = await QueryAsync(factory, async db =>
                (await db.Users.CountAsync(), await db.Projects.CountAsync(), await db.UserSubscriptions.CountAsync(), await db.UserCircadianProfiles.CountAsync()));

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal((0, 0, 0, 0), leftovers);

            // Hết sự cố thì đăng nhập lại bình thường và đủ dữ liệu
            factory.FailOnProjectCreate = false;
            var (_, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            Assert.Equal(1, await QueryAsync(factory, db => db.Projects.CountAsync(p => p.UserId == auth.UserId)));
        }

        [Fact]
        public async Task MisconfiguredFreePlan_BlocksFirstLogin_AndCreatesNothing()
        {
            using var factory = new ApiFactory();
            await factory.EnsureDefaultRoleAsync();
            await factory.SeedAsync(db => db.SubscriptionPlans.Single(p => p.Id == ApiFactory.FreePlanId).Price = 79000);

            var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/google", new GoogleLoginRequestDto
            {
                IdToken = "valid:user-1:user-1@test.com:Test user-1"
            });

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(0, await QueryAsync(factory, db => db.Users.CountAsync()));
        }

        // ------------------------------------------------------------ Bù dữ liệu user cũ

        [Fact]
        public async Task Backfill_FillsMissingData_KeepsExistingRows_AndIsSafeToRepeat()
        {
            using var factory = new ApiFactory();
            await factory.EnsureDefaultRoleAsync();
            await factory.SeedUserWithRoleAsync("legacy", "Freelancer");
            var userId = await QueryAsync(factory, db => db.Users.Select(u => u.Id).SingleAsync());
            await factory.SeedAsync(db => db.UserCircadianProfiles.Add(new UserCircadianProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                HourOfDay = 9,
                Score = 0.900m,
                Source = "Onboarding",
                UpdatedAt = DateTime.UtcNow
            }));

            var first = await UserBackfillRunner.RunAsync(factory.Services);
            var second = await UserBackfillRunner.RunAsync(factory.Services);

            var circadian = await QueryAsync(factory, db => db.UserCircadianProfiles.Where(c => c.UserId == userId).ToListAsync());
            Assert.Equal(new UserBackfillRunner.Result(1, 1, 0), first);
            Assert.Equal(new UserBackfillRunner.Result(0, 0, 0), second);
            Assert.Equal(24, circadian.Count);
            Assert.Equal(0.900m, circadian.Single(c => c.HourOfDay == 9).Score);
            Assert.Equal(1, await QueryAsync(factory, db => db.Projects.CountAsync(p => p.UserId == userId && p.IsDefault)));
            Assert.Equal(1, await QueryAsync(factory, db => db.UserSubscriptions.CountAsync(s => s.UserId == userId && s.Status == "Active")));
        }

        [Fact]
        public async Task Backfill_DoesNotGiveFreePlan_ToUserWhoAlreadyHasActivePaidPlan()
        {
            using var factory = new ApiFactory();
            await factory.EnsureDefaultRoleAsync();
            await factory.SeedUserWithRoleAsync("legacy", "Freelancer");
            var userId = await QueryAsync(factory, db => db.Users.Select(u => u.Id).SingleAsync());
            var paidPlanId = Guid.NewGuid();
            await factory.SeedAsync(db =>
            {
                db.SubscriptionPlans.Add(new SubscriptionPlan { Id = paidPlanId, Name = "Pro", Price = 99000, DurationDays = 30, StorageLimitMb = 1024, IsActive = true });
                db.UserSubscriptions.Add(new UserSubscription
                {
                    Id = Guid.NewGuid(), UserId = userId, PlanId = paidPlanId, Status = "Active",
                    StartAt = DateTime.UtcNow, EndAt = DateTime.UtcNow.AddDays(10), QuotaResetAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
                });
            });

            await UserBackfillRunner.RunAsync(factory.Services);

            var subscription = Assert.Single(await QueryAsync(factory, db => db.UserSubscriptions.Where(s => s.UserId == userId).ToListAsync()));
            Assert.Equal(paidPlanId, subscription.PlanId);
        }

        // ------------------------------------------------------------ Hết hạn gói trả phí

        [Fact]
        public async Task ExpiredPaidPlan_FallsBackToFreePlan_WithExactlyOneActive()
        {
            using var factory = new ApiFactory();
            var (_, user, userId, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);
            await PaymentFlowTests.PayAsync(factory, checkout);
            await factory.SeedAsync(db =>
            {
                var paid = db.UserSubscriptions.Single(s => s.UserId == userId && s.PlanId != ApiFactory.FreePlanId);
                paid.StartAt = DateTime.UtcNow.AddDays(-31);
                paid.EndAt = DateTime.UtcNow.AddMinutes(-1);
            });

            using (var scope = factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IPaymentService>().RefreshSubscriptionStatesAsync();
            }

            var subscriptions = await QueryAsync(factory, db => db.UserSubscriptions.Where(s => s.UserId == userId).ToListAsync());
            var active = Assert.Single(subscriptions, s => s.Status == "Active");
            Assert.Equal(ApiFactory.FreePlanId, active.PlanId);
            Assert.Null(active.EndAt);
            Assert.Equal("Expired", subscriptions.Single(s => s.PlanId == planId).Status);

            // Chạy lại job: không sinh thêm gói Miễn phí
            using (var scope = factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IPaymentService>().RefreshSubscriptionStatesAsync();
            }

            Assert.Equal(1, await QueryAsync(factory, db => db.UserSubscriptions.CountAsync(s => s.UserId == userId && s.Status == "Active")));
        }

        // ------------------------------------------------------------ GET /api/me

        [Fact]
        public async Task GetMe_ReturnsProfile_FreePlan_AndGoogleNotConnected()
        {
            using var factory = new ApiFactory();
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");

            var me = await client.GetFromJsonAsync<MeDto>("/api/me");

            Assert.Equal(auth.UserId, me!.Id);
            Assert.False(me.OnboardingCompleted);
            Assert.Equal("Miễn phí", me.CurrentSubscription!.PlanName);
            Assert.Null(me.CurrentSubscription.EndAt);
            Assert.Equal("NotConnected", me.GoogleCalendarStatus);
        }

        [Fact]
        public async Task GetMe_ReportsGoogleConnectionStatus()
        {
            using var factory = new ApiFactory();
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            await factory.SeedAsync(db => db.GoogleConnections.Add(new GoogleConnection
            {
                Id = Guid.NewGuid(),
                UserId = auth.UserId,
                AccessTokenEncrypted = new byte[] { 1 },
                RefreshTokenEncrypted = new byte[] { 1 },
                TokenExpiresAt = DateTime.UtcNow.AddHours(1),
                Status = "Expired",
                CreatedAt = DateTime.UtcNow
            }));

            var me = await client.GetFromJsonAsync<MeDto>("/api/me");

            Assert.Equal("Expired", me!.GoogleCalendarStatus);
        }

        [Fact]
        public async Task Me_RequiresLogin()
        {
            using var factory = new ApiFactory();

            var response = await factory.CreateClient().GetAsync("/api/me");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // ------------------------------------------------------------ PUT /api/me/settings

        private static UpdateSettingsRequestDto Settings() => new()
        {
            WorkDayStart = new TimeOnly(7, 30),
            WorkDayEnd = new TimeOnly(16, 0),
            WorksOnWeekend = true,
            DefaultBufferMinutes = 10,
            ReminderLeadMinutes = 30,
            TimeZone = "Asia/Bangkok"
        };

        [Fact]
        public async Task UpdateSettings_SavesValues()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");

            var response = await client.PutAsJsonAsync("/api/me/settings", Settings());
            var me = await response.Content.ReadFromJsonAsync<MeDto>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new TimeOnly(7, 30), me!.WorkDayStart);
            Assert.Equal(new TimeOnly(16, 0), me.WorkDayEnd);
            Assert.True(me.WorksOnWeekend);
            Assert.Equal(10, me.DefaultBufferMinutes);
            Assert.Equal(30, me.ReminderLeadMinutes);
            Assert.Equal("Asia/Bangkok", me.TimeZone);
        }

        [Fact]
        public async Task UpdateSettings_RejectsValuesThatBreakDbChecks()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");

            var endBeforeStart = Settings();
            endBeforeStart.WorkDayEnd = endBeforeStart.WorkDayStart;
            var bufferTooBig = Settings();
            bufferTooBig.DefaultBufferMinutes = 121;
            var reminderNegative = Settings();
            reminderNegative.ReminderLeadMinutes = -1;
            var badZone = Settings();
            badZone.TimeZone = "Mars/Olympus";

            foreach (var request in new[] { endBeforeStart, bufferTooBig, reminderNegative, badZone })
            {
                var response = await client.PutAsJsonAsync("/api/me/settings", request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
        }

        // ------------------------------------------------------------ POST /api/me/onboarding

        [Fact]
        public async Task Onboarding_SetsCircadianScores_DomainProfiles_AndFlag()
        {
            using var factory = new ApiFactory();
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, _) = await TestHelpers.LoginAsync(factory, "admin-1");
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            var writing = await CreateDomainAsync(admin, "Writing");

            var response = await client.PostAsJsonAsync("/api/me/onboarding", Onboarding(new[] { writing.Id }, new[] { 9, 10 }, "Often"));
            var me = await response.Content.ReadFromJsonAsync<MeDto>();

            var circadian = (await QueryAsync(factory, db => db.UserCircadianProfiles.Where(c => c.UserId == auth.UserId).ToListAsync()))
                .ToDictionary(c => (int)c.HourOfDay, c => c.Score);
            var profile = Assert.Single(await QueryAsync(factory, db => db.UserDomainProfiles.Where(p => p.UserId == auth.UserId).ToListAsync()));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(me!.OnboardingCompleted);
            Assert.Equal(new TimeOnly(9, 0), me.WorkDayStart);
            Assert.True(me.WorksOnWeekend);
            Assert.Equal(0.800m, circadian[9]);   // giờ hiệu quả
            Assert.Equal(0.800m, circadian[10]);
            Assert.Equal(0.500m, circadian[11]);  // trong khung 9-17 nhưng không đánh dấu hiệu quả
            Assert.Equal(0.500m, circadian[16]);
            Assert.Equal(0.200m, circadian[8]);   // ngoài khung
            Assert.Equal(0.200m, circadian[17]);
            Assert.Equal(0.200m, circadian[0]);
            Assert.Equal(24, circadian.Count);
            Assert.Equal(1.500m, profile.Kbias);
            Assert.Equal(0, profile.SampleCount);
        }

        [Fact]
        public async Task Onboarding_TwiceOverwrites_WithoutDuplicates()
        {
            using var factory = new ApiFactory();
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, _) = await TestHelpers.LoginAsync(factory, "admin-1");
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            var writing = await CreateDomainAsync(admin, "Writing");
            var design = await CreateDomainAsync(admin, "Design");

            await client.PostAsJsonAsync("/api/me/onboarding", Onboarding(new[] { writing.Id }, new[] { 9, 10 }, "Rarely"));
            var second = Onboarding(new[] { writing.Id, design.Id }, new[] { 14 }, "Always");
            second.WorkDayStart = new TimeOnly(10, 0);
            second.WorkDayEnd = new TimeOnly(15, 0);
            var response = await client.PostAsJsonAsync("/api/me/onboarding", second);

            var circadian = (await QueryAsync(factory, db => db.UserCircadianProfiles.Where(c => c.UserId == auth.UserId).ToListAsync()))
                .ToDictionary(c => (int)c.HourOfDay, c => c.Score);
            var profiles = await QueryAsync(factory, db => db.UserDomainProfiles.Where(p => p.UserId == auth.UserId).ToListAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(24, circadian.Count);
            Assert.Equal(0.800m, circadian[14]);  // giờ hiệu quả của lần sau
            Assert.Equal(0.500m, circadian[10]);  // lần trước là hiệu quả, lần sau chỉ là giờ làm việc
            Assert.Equal(0.200m, circadian[9]);   // lần sau nằm ngoài khung 10-15
            Assert.Equal(2, profiles.Count);
            Assert.Equal(2, profiles.Select(p => p.DomainId).Distinct().Count());
            Assert.All(profiles, p => Assert.Equal(1.800m, p.Kbias));
        }

        [Fact]
        public async Task Onboarding_KeepsKBiasAlreadyLearnedFromRealTasks()
        {
            using var factory = new ApiFactory();
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, _) = await TestHelpers.LoginAsync(factory, "admin-1");
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            var writing = await CreateDomainAsync(admin, "Writing");
            await client.PostAsJsonAsync("/api/me/onboarding", Onboarding(new[] { writing.Id }, new[] { 9 }, "Sometimes"));
            await factory.SeedAsync(db =>
            {
                var profile = db.UserDomainProfiles.Single(p => p.UserId == auth.UserId);
                profile.Kbias = 1.337m;
                profile.SampleCount = 8;
            });

            await client.PostAsJsonAsync("/api/me/onboarding", Onboarding(new[] { writing.Id }, new[] { 9 }, "Always"));

            var profile = await QueryAsync(factory, db => db.UserDomainProfiles.SingleAsync(p => p.UserId == auth.UserId));
            Assert.Equal(1.337m, profile.Kbias);
            Assert.Equal(8, profile.SampleCount);
        }

        [Fact]
        public async Task Onboarding_RejectsBadInput_AndSavesNothing()
        {
            using var factory = new ApiFactory();
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, _) = await TestHelpers.LoginAsync(factory, "admin-1");
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            var active = await CreateDomainAsync(admin, "Writing");
            var inactive = await CreateDomainAsync(admin, "Legacy", isActive: false);

            var inactiveDomain = Onboarding(new[] { inactive.Id }, new[] { 9 });
            var unknownDomain = Onboarding(new[] { Guid.NewGuid() }, new[] { 9 });
            var hour24 = Onboarding(new[] { active.Id }, new[] { 24 });
            var badLevel = Onboarding(new[] { active.Id }, new[] { 9 }, "Sometimez");
            var endBeforeStart = Onboarding(new[] { active.Id }, new[] { 9 });
            endBeforeStart.WorkDayEnd = endBeforeStart.WorkDayStart;

            foreach (var request in new[] { inactiveDomain, unknownDomain, hour24, badLevel, endBeforeStart })
            {
                var response = await client.PostAsJsonAsync("/api/me/onboarding", request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }

            var me = await client.GetFromJsonAsync<MeDto>("/api/me");
            Assert.False(me!.OnboardingCompleted);
            Assert.Equal(0, await QueryAsync(factory, db => db.UserDomainProfiles.CountAsync(p => p.UserId == auth.UserId)));
        }
    }
}
