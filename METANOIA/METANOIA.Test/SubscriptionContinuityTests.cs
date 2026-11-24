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
    // Người dùng không bao giờ bị để trống gói: hết hạn hoặc bị hoàn tiền thì có gói xếp hàng bật lên, không có thì về gói Miễn phí
    public class SubscriptionContinuityTests
    {
        private static async Task<List<UserSubscription>> SubscriptionsAsync(ApiFactory factory, Guid userId)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .UserSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        }

        // ------------------------------------------------------------ GET /api/me xử lý gói hết hạn ngay

        [Fact]
        public async Task GetMe_AfterThePaidPlanExpired_AlreadyShowsTheFreePlan_WithoutWaitingForTheJob()
        {
            using var factory = new ApiFactory();
            var (_, user, userId, planId) = await PaymentFlowTests.SetupAsync(factory);
            await PaymentFlowTests.PayAsync(factory, await PaymentFlowTests.CheckoutAsync(user, planId));
            var before = await user.GetFromJsonAsync<MeDto>("/api/me");
            await factory.SeedAsync(db =>
            {
                var paid = db.UserSubscriptions.Single(s => s.UserId == userId && s.PlanId == planId);
                paid.StartAt = DateTime.UtcNow.AddDays(-31);
                paid.EndAt = DateTime.UtcNow.AddMinutes(-1);
            });

            var me = await user.GetFromJsonAsync<MeDto>("/api/me");
            var subscriptions = await SubscriptionsAsync(factory, userId);

            Assert.Equal("Pro", before!.CurrentSubscription!.PlanName);
            Assert.Equal("Miễn phí", me!.CurrentSubscription!.PlanName);
            Assert.Null(me.CurrentSubscription.EndAt);
            Assert.Equal(1, subscriptions.Count(s => s.Status == "Active"));
            Assert.Equal("Expired", subscriptions.Single(s => s.PlanId == planId).Status);
        }

        [Fact]
        public async Task GetMe_WhenTheQueuedPlanIsDue_ShowsItInsteadOfTheFreePlan()
        {
            using var factory = new ApiFactory();
            var (_, user, userId, planId) = await PaymentFlowTests.SetupAsync(factory);
            await PaymentFlowTests.PayAsync(factory, await PaymentFlowTests.CheckoutAsync(user, planId));
            await PaymentFlowTests.PayAsync(factory, await PaymentFlowTests.CheckoutAsync(user, planId));
            await factory.SeedAsync(db =>
            {
                var active = db.UserSubscriptions.Single(s => s.UserId == userId && s.Status == "Active");
                var queued = db.UserSubscriptions.Single(s => s.UserId == userId && s.Status == "Scheduled");
                active.StartAt = DateTime.UtcNow.AddDays(-31);
                active.EndAt = DateTime.UtcNow.AddMinutes(-1);
                queued.StartAt = active.EndAt.Value;
            });

            var me = await user.GetFromJsonAsync<MeDto>("/api/me");
            var subscriptions = await SubscriptionsAsync(factory, userId);

            Assert.Equal("Pro", me!.CurrentSubscription!.PlanName);
            Assert.Equal(1, subscriptions.Count(s => s.Status == "Active"));
            Assert.DoesNotContain(subscriptions, s => s.Status == "Scheduled");
        }

        [Fact]
        public async Task GetMe_WithAValidPlan_ChangesNothing()
        {
            using var factory = new ApiFactory();
            var (_, user, userId, planId) = await PaymentFlowTests.SetupAsync(factory);
            await PaymentFlowTests.PayAsync(factory, await PaymentFlowTests.CheckoutAsync(user, planId));
            var before = (await SubscriptionsAsync(factory, userId)).Select(s => (s.Id, s.Status)).OrderBy(s => s.Id).ToList();

            await user.GetFromJsonAsync<MeDto>("/api/me");
            await user.GetFromJsonAsync<MeDto>("/api/me");

            var after = (await SubscriptionsAsync(factory, userId)).Select(s => (s.Id, s.Status)).OrderBy(s => s.Id).ToList();
            Assert.Equal(before, after);
        }
    }
}
