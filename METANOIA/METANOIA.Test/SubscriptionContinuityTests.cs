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

        private static async Task<RefundDto> RefundAsync(HttpClient user, Guid paymentId)
        {
            var response = await user.PostAsJsonAsync($"/api/payments/{paymentId}/refunds", new RefundRequestDto { Reason = "Mua nhầm" });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<RefundDto>())!;
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

        // ------------------------------------------------------------ Duyệt hoàn tiền không để user trống gói

        [Fact]
        public async Task ApprovingARefund_ForTheActivePaidPlan_MovesTheUserBackToTheFreePlan()
        {
            using var factory = new ApiFactory();
            var (admin, user, userId, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);
            await PaymentFlowTests.PayAsync(factory, checkout);
            var refund = await RefundAsync(user, checkout.PaymentId);

            var response = await admin.PostAsJsonAsync($"/api/refunds/{refund.Id}/approve", new RefundProcessDto { Note = "OK" });
            var me = await user.GetFromJsonAsync<MeDto>("/api/me");
            var subscriptions = await SubscriptionsAsync(factory, userId);

            response.EnsureSuccessStatusCode();
            Assert.Equal("Cancelled", subscriptions.Single(s => s.PlanId == planId).Status);
            var active = Assert.Single(subscriptions, s => s.Status == "Active");
            Assert.Equal(ApiFactory.FreePlanId, active.PlanId);
            Assert.Null(active.EndAt);
            Assert.Equal("Miễn phí", me!.CurrentSubscription!.PlanName);
        }

        [Fact]
        public async Task ApprovingARefund_WhileAnotherPaidPlanIsQueued_StartsThatPlanNow_InsteadOfTheFreeOne()
        {
            using var factory = new ApiFactory();
            var (admin, user, userId, planId) = await PaymentFlowTests.SetupAsync(factory);
            var first = await PaymentFlowTests.CheckoutAsync(user, planId);
            await PaymentFlowTests.PayAsync(factory, first);
            await PaymentFlowTests.PayAsync(factory, await PaymentFlowTests.CheckoutAsync(user, planId));
            var queuedLength = (await SubscriptionsAsync(factory, userId)).Single(s => s.Status == "Scheduled")
                .Let(s => s.EndAt!.Value - s.StartAt);
            var refund = await RefundAsync(user, first.PaymentId);

            await admin.PostAsJsonAsync($"/api/refunds/{refund.Id}/approve", new RefundProcessDto());
            var subscriptions = await SubscriptionsAsync(factory, userId);

            var active = Assert.Single(subscriptions, s => s.Status == "Active");
            Assert.NotEqual(ApiFactory.FreePlanId, active.PlanId);
            Assert.True(active.StartAt <= DateTime.UtcNow);
            Assert.True(Math.Abs((active.EndAt!.Value - active.StartAt - queuedLength).TotalSeconds) < 1);
            Assert.Equal("Cancelled", subscriptions.Single(s => s.PaymentTransactionId == first.PaymentId).Status);
            Assert.DoesNotContain(subscriptions, s => s.Status == "Scheduled");
        }

        [Fact]
        public async Task ApprovingARefund_ForAQueuedPlan_LeavesTheCurrentPlanUntouched()
        {
            using var factory = new ApiFactory();
            var (admin, user, userId, planId) = await PaymentFlowTests.SetupAsync(factory);
            await PaymentFlowTests.PayAsync(factory, await PaymentFlowTests.CheckoutAsync(user, planId));
            var second = await PaymentFlowTests.CheckoutAsync(user, planId);
            await PaymentFlowTests.PayAsync(factory, second);
            var currentId = (await SubscriptionsAsync(factory, userId)).Single(s => s.Status == "Active").Id;
            var refund = await RefundAsync(user, second.PaymentId);

            await admin.PostAsJsonAsync($"/api/refunds/{refund.Id}/approve", new RefundProcessDto());
            var subscriptions = await SubscriptionsAsync(factory, userId);

            Assert.Equal(currentId, Assert.Single(subscriptions, s => s.Status == "Active").Id);
            Assert.Equal("Cancelled", subscriptions.Single(s => s.PaymentTransactionId == second.PaymentId).Status);
            Assert.DoesNotContain(subscriptions, s => s.PlanId == ApiFactory.FreePlanId && s.Status == "Active");
        }

        [Fact]
        public async Task ApprovingARefund_AfterThePlanAlreadyExpired_DoesNotCreateAnotherFreePlan()
        {
            using var factory = new ApiFactory();
            var (admin, user, userId, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);
            await PaymentFlowTests.PayAsync(factory, checkout);
            var refund = await RefundAsync(user, checkout.PaymentId);
            await factory.SeedAsync(db =>
            {
                var paid = db.UserSubscriptions.Single(s => s.UserId == userId && s.PlanId == planId);
                paid.StartAt = DateTime.UtcNow.AddDays(-31);
                paid.EndAt = DateTime.UtcNow.AddMinutes(-1);
            });
            await user.GetFromJsonAsync<MeDto>("/api/me"); // gói hết hạn, user đã về gói Miễn phí

            await admin.PostAsJsonAsync($"/api/refunds/{refund.Id}/approve", new RefundProcessDto());
            var subscriptions = await SubscriptionsAsync(factory, userId);

            Assert.Equal(1, subscriptions.Count(s => s.Status == "Active"));
            Assert.Equal(ApiFactory.FreePlanId, subscriptions.Single(s => s.Status == "Active").PlanId);
        }
    }

    internal static class ObjectExtensions
    {
        public static TResult Let<T, TResult>(this T value, Func<T, TResult> selector) => selector(value);
    }
}
