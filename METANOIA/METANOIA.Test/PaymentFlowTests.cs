using System.Net;
using System.Net.Http.Json;
using System.Text;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Infrastructure.Data;
using METANOIA.Test.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // Các tình huống lỗi của luồng thanh toán PayOS: mạng yếu, webhook trùng/lỡ/giả, tiền về sai, hết hạn, hủy...
    public class PaymentFlowTests
    {
        internal static async Task<(HttpClient Admin, HttpClient User, Guid UserId, Guid PlanId)> SetupAsync(ApiFactory factory)
        {
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, _) = await TestHelpers.LoginAsync(factory, "admin-1");
            var (user, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            var plan = await (await admin.PostAsJsonAsync("/api/subscription-plans", new SubscriptionPlanRequestDto
            {
                Name = "Pro",
                Price = 99000,
                DurationDays = 30,
                AiCallLimit = 500,
                StorageLimitMb = 2048
            })).Content.ReadFromJsonAsync<SubscriptionPlanDto>();
            return (admin, user, auth.UserId, plan!.Id);
        }

        internal static async Task<CheckoutResultDto> CheckoutAsync(HttpClient user, Guid planId)
        {
            var response = await user.PostAsJsonAsync("/api/payments/checkout", new CheckoutRequestDto { PlanId = planId });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<CheckoutResultDto>())!;
        }

        internal static Task<HttpResponseMessage> SendWebhookAsync(ApiFactory factory, string body)
        {
            return factory.CreateClient().PostAsync("/api/payos/webhook", new StringContent(body, Encoding.UTF8, "application/json"));
        }

        internal static async Task<PaymentTransactionDto> GetPaymentAsync(HttpClient client, Guid paymentId)
        {
            return (await client.GetFromJsonAsync<PaymentTransactionDto>($"/api/payment-transactions/{paymentId}"))!;
        }

        internal static async Task ModifyPaymentAsync(ApiFactory factory, Guid paymentId, Action<METANOIA.Domain.Entities.PaymentTransaction> change)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var payment = await db.PaymentTransactions.FirstAsync(p => p.Id == paymentId);
            change(payment);
            await db.SaveChangesAsync();
        }

        internal static async Task PayAsync(ApiFactory factory, CheckoutResultDto checkout, long amount = 99000)
        {
            var response = await SendWebhookAsync(factory, FakePayOSClient.WebhookBody(checkout.OrderCode, amount));
            response.EnsureSuccessStatusCode();
        }

        // Các gói đã mua; bỏ qua gói Miễn phí mà mọi user mới đều có sẵn
        internal static async Task<List<UserSubscriptionDto>> GetSubscriptionsAsync(HttpClient user)
        {
            var all = await user.GetFromJsonAsync<List<UserSubscriptionDto>>("/api/user-subscriptions");
            return all!.Where(s => s.PlanId != ApiFactory.FreePlanId).ToList();
        }

        // ------------------------------------------------------------ Checkout

        [Fact]
        public async Task Checkout_CreatesPendingPayment_WithCheckoutUrl()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);

            var checkout = await CheckoutAsync(user, planId);
            var payment = await GetPaymentAsync(user, checkout.PaymentId);

            Assert.Equal("Pending", payment.Status);
            Assert.Equal(99000, payment.Amount);
            Assert.StartsWith("https://pay.payos.vn/", checkout.CheckoutUrl);
        }

        [Fact]
        public async Task Checkout_Twice_ReusesTheOpenLink_InsteadOfCreatingAnotherOrder()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);

            var first = await CheckoutAsync(user, planId);
            var second = await CheckoutAsync(user, planId);
            var payments = await user.GetFromJsonAsync<List<PaymentTransactionDto>>("/api/payment-transactions");

            Assert.Equal(first.PaymentId, second.PaymentId);
            Assert.True(second.Reused);
            Assert.Equal(first.CheckoutUrl, second.CheckoutUrl);
            Assert.Single(payments!);
            Assert.Equal(1, factory.PayOS.CreateCalls);
        }

        [Fact]
        public async Task Checkout_WhenPayOSDown_Returns503_AndMarksPaymentFailed_NotStuckPending()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            factory.PayOS.FailOnCreate = true;

            var response = await user.PostAsJsonAsync("/api/payments/checkout", new CheckoutRequestDto { PlanId = planId });
            var payments = await user.GetFromJsonAsync<List<PaymentTransactionDto>>("/api/payment-transactions");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("Failed", Assert.Single(payments!).Status);

            factory.PayOS.FailOnCreate = false;
            var retry = await CheckoutAsync(user, planId);
            Assert.Equal("Pending", retry.Status);
        }

        [Fact]
        public async Task Checkout_WhenOldLinkWasPaidMeanwhile_ReturnsSucceeded_AndActivatesPlan()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var first = await CheckoutAsync(user, planId);
            factory.PayOS.Info[first.OrderCode] = factory.PayOS.Info[first.OrderCode] with { Status = "PAID", AmountPaid = 99000 };

            var second = await CheckoutAsync(user, planId);
            var subscriptions = await GetSubscriptionsAsync(user);

            Assert.Equal("Succeeded", second.Status);
            Assert.Null(second.CheckoutUrl);
            Assert.Single(subscriptions);
            Assert.Equal(1, factory.PayOS.CreateCalls);
        }

        [Fact]
        public async Task Checkout_ForFreeOrUnknownPlan_Returns400()
        {
            using var factory = new ApiFactory();
            var (admin, user, _, _) = await SetupAsync(factory);
            var free = await (await admin.PostAsJsonAsync("/api/subscription-plans", new SubscriptionPlanRequestDto
            {
                Name = "Free", Price = 0, DurationDays = 30, StorageLimitMb = 100
            })).Content.ReadFromJsonAsync<SubscriptionPlanDto>();

            var freeResponse = await user.PostAsJsonAsync("/api/payments/checkout", new CheckoutRequestDto { PlanId = free!.Id });
            var unknownResponse = await user.PostAsJsonAsync("/api/payments/checkout", new CheckoutRequestDto { PlanId = Guid.NewGuid() });

            Assert.Equal(HttpStatusCode.BadRequest, freeResponse.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, unknownResponse.StatusCode);
        }

        // ------------------------------------------------------------ Webhook

        [Fact]
        public async Task Webhook_Paid_MarksSucceeded_AndActivatesSubscription()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);

            var response = await SendWebhookAsync(factory, FakePayOSClient.WebhookBody(checkout.OrderCode, 99000));
            var payment = await GetPaymentAsync(user, checkout.PaymentId);
            var subscription = Assert.Single(await GetSubscriptionsAsync(user));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Succeeded", payment.Status);
            Assert.NotNull(payment.PaidAt);
            Assert.NotNull(payment.WebhookReceivedAt);
            Assert.Equal("Active", subscription.Status);
            Assert.Equal(payment.Id, subscription.PaymentTransactionId);
        }

        [Fact]
        public async Task Webhook_Duplicate_DoesNotActivateTwice()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);

            await PayAsync(factory, checkout);
            var again = await SendWebhookAsync(factory, FakePayOSClient.WebhookBody(checkout.OrderCode, 99000));

            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            Assert.Contains("Duplicate", await again.Content.ReadAsStringAsync());
            Assert.Single(await GetSubscriptionsAsync(user));
        }

        [Fact]
        public async Task Webhook_WithInvalidSignature_Returns400_AndChangesNothing()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);

            var response = await SendWebhookAsync(factory, FakePayOSClient.WebhookBody(checkout.OrderCode, 99000, signature: "forged"));
            var payment = await GetPaymentAsync(user, checkout.PaymentId);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Pending", payment.Status);
            Assert.Empty(await GetSubscriptionsAsync(user));
        }

        [Fact]
        public async Task Webhook_ForUnknownOrderCode_Returns200_SoPayOSStopsRetrying()
        {
            using var factory = new ApiFactory();
            await SetupAsync(factory);

            var response = await SendWebhookAsync(factory, FakePayOSClient.WebhookBody(123, 3000));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Ignored", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Webhook_AmountMismatch_FailsPayment_AndOpensRefund_WithoutActivating()
        {
            using var factory = new ApiFactory();
            var (admin, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);

            await PayAsync(factory, checkout, amount: 50000);
            var payment = await GetPaymentAsync(user, checkout.PaymentId);
            var refunds = await admin.GetFromJsonAsync<List<RefundDto>>("/api/refunds");

            Assert.Equal("Failed", payment.Status);
            Assert.Empty(await GetSubscriptionsAsync(user));
            var refund = Assert.Single(refunds!);
            Assert.Equal("System", refund.Source);
            Assert.Equal("Requested", refund.Status);
            Assert.Equal(50000, refund.Amount);
        }

        [Fact]
        public async Task Webhook_AfterUserCancelled_OpensRefund_InsteadOfActivating()
        {
            using var factory = new ApiFactory();
            var (admin, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);
            await user.PostAsync($"/api/payments/{checkout.PaymentId}/cancel", null);

            await PayAsync(factory, checkout);
            await PayAsync(factory, checkout); // PayOS gửi lại: không mở thêm yêu cầu hoàn tiền thứ hai
            var refunds = await admin.GetFromJsonAsync<List<RefundDto>>("/api/refunds");

            Assert.Equal("Cancelled", (await GetPaymentAsync(user, checkout.PaymentId)).Status);
            Assert.Empty(await GetSubscriptionsAsync(user));
            Assert.Equal(99000, Assert.Single(refunds!).Amount);
        }

        [Fact]
        public async Task Webhook_LateAfterExpiry_StillActivatesPlan_BecauseMoneyWasReceived()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);
            await ModifyPaymentAsync(factory, checkout.PaymentId, p => p.Status = "Expired");

            await PayAsync(factory, checkout);

            Assert.Equal("Succeeded", (await GetPaymentAsync(user, checkout.PaymentId)).Status);
            Assert.Single(await GetSubscriptionsAsync(user));
        }

        [Fact]
        public async Task Webhook_SecondPurchaseWhileActive_QueuesAsScheduled()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            await PayAsync(factory, await CheckoutAsync(user, planId));
            await PayAsync(factory, await CheckoutAsync(user, planId));

            var subscriptions = await GetSubscriptionsAsync(user);

            Assert.Equal(2, subscriptions.Count);
            var scheduled = Assert.Single(subscriptions, s => s.Status == "Scheduled");
            var active = Assert.Single(subscriptions, s => s.Status == "Active");
            Assert.Equal(active.EndAt, scheduled.StartAt);
        }

        // ------------------------------------------------------------ Sync (webhook bị lỡ / mạng yếu)

        [Fact]
        public async Task Sync_WhenWebhookWasMissed_PullsPaidStatusFromPayOS()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);
            factory.PayOS.Info[checkout.OrderCode] = factory.PayOS.Info[checkout.OrderCode] with { Status = "PAID", AmountPaid = 99000 };

            var response = await user.PostAsync($"/api/payments/{checkout.PaymentId}/sync", null);
            var synced = await response.Content.ReadFromJsonAsync<PaymentTransactionDto>();

            Assert.Equal("Succeeded", synced!.Status);
            Assert.Single(await GetSubscriptionsAsync(user));
        }

        [Fact]
        public async Task Sync_WhenPayOSUnreachable_Returns503_AndKeepsPending()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);
            factory.PayOS.FailOnGetInfo = true;

            var response = await user.PostAsync($"/api/payments/{checkout.PaymentId}/sync", null);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("Pending", (await GetPaymentAsync(user, checkout.PaymentId)).Status);
        }

        [Fact]
        public async Task Sync_OtherUsersPayment_Returns404()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);
            var (stranger, _) = await TestHelpers.LoginAsync(factory, "stranger");

            var response = await stranger.PostAsync($"/api/payments/{checkout.PaymentId}/sync", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // ------------------------------------------------------------ Cancel

        [Fact]
        public async Task Cancel_Pending_MarksCancelled_AndIsIdempotent()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);

            var first = await user.PostAsync($"/api/payments/{checkout.PaymentId}/cancel", null);
            var second = await user.PostAsync($"/api/payments/{checkout.PaymentId}/cancel", null);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal("Cancelled", (await GetPaymentAsync(user, checkout.PaymentId)).Status);
            Assert.Equal(1, factory.PayOS.CancelCalls);
        }

        [Fact]
        public async Task Cancel_WhenPayOSUnreachable_Returns503_AndKeepsPending()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);
            factory.PayOS.FailOnCancel = true;

            var response = await user.PostAsync($"/api/payments/{checkout.PaymentId}/cancel", null);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("Pending", (await GetPaymentAsync(user, checkout.PaymentId)).Status);
        }

        [Fact]
        public async Task Cancel_WhenCustomerAlreadyPaid_ReturnsSucceeded_InsteadOfCancelling()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);
            factory.PayOS.Info[checkout.OrderCode] = factory.PayOS.Info[checkout.OrderCode] with { Status = "PAID", AmountPaid = 99000 };
            factory.PayOS.CancelResult = PayOSCancelResult.NotCancellable;

            var response = await user.PostAsync($"/api/payments/{checkout.PaymentId}/cancel", null);
            var payment = await response.Content.ReadFromJsonAsync<PaymentTransactionDto>();

            Assert.Equal("Succeeded", payment!.Status);
            Assert.Single(await GetSubscriptionsAsync(user));
        }

        [Fact]
        public async Task Cancel_SucceededPayment_Returns400()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);
            await PayAsync(factory, checkout);

            var response = await user.PostAsync($"/api/payments/{checkout.PaymentId}/cancel", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // ------------------------------------------------------------ Đối soát nền

        [Fact]
        public async Task Reconcile_FindsStalePending_AndRecoversPaidOrder()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);
            await ModifyPaymentAsync(factory, checkout.PaymentId, p => p.CreatedAt = DateTime.UtcNow.AddMinutes(-10));
            factory.PayOS.Info[checkout.OrderCode] = factory.PayOS.Info[checkout.OrderCode] with { Status = "PAID", AmountPaid = 99000 };

            using var scope = factory.Services.CreateScope();
            var payments = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var ids = await payments.GetPaymentIdsNeedingReconciliationAsync();
            foreach (var id in ids)
            {
                await payments.ReconcilePaymentAsync(id);
            }

            Assert.Contains(checkout.PaymentId, ids);
            Assert.Equal("Succeeded", (await GetPaymentAsync(user, checkout.PaymentId)).Status);
            Assert.Single(await GetSubscriptionsAsync(user));
        }

        [Fact]
        public async Task Reconcile_PendingPastExpiry_ClosesLinkAtPayOS_AndMarksExpired()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);
            await ModifyPaymentAsync(factory, checkout.PaymentId, p =>
            {
                p.CreatedAt = DateTime.UtcNow.AddMinutes(-30);
                p.ExpiresAt = DateTime.UtcNow.AddMinutes(-15);
            });

            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IPaymentService>().ReconcilePaymentAsync(checkout.PaymentId);

            Assert.Equal("Expired", (await GetPaymentAsync(user, checkout.PaymentId)).Status);
            Assert.Equal(1, factory.PayOS.CancelCalls);
        }

        [Fact]
        public async Task Reconcile_WhenPayOSUnreachable_LeavesPendingUntouched()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);
            await ModifyPaymentAsync(factory, checkout.PaymentId, p => p.ExpiresAt = DateTime.UtcNow.AddMinutes(-15));
            factory.PayOS.FailOnGetInfo = true;

            using var scope = factory.Services.CreateScope();
            var payments = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            await Assert.ThrowsAsync<METANOIA.Application.Exceptions.PaymentGatewayException>(
                () => payments.ReconcilePaymentAsync(checkout.PaymentId));

            Assert.Equal("Pending", (await GetPaymentAsync(user, checkout.PaymentId)).Status);
        }

        [Fact]
        public async Task Reconcile_HealsSucceededPaymentThatHasNoSubscription()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            var checkout = await CheckoutAsync(user, planId);
            await ModifyPaymentAsync(factory, checkout.PaymentId, p => { p.Status = "Succeeded"; p.PaidAt = DateTime.UtcNow; });

            using var scope = factory.Services.CreateScope();
            var payments = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var ids = await payments.GetPaymentIdsNeedingReconciliationAsync();
            await payments.ReconcilePaymentAsync(checkout.PaymentId);

            Assert.Contains(checkout.PaymentId, ids);
            Assert.Single(await GetSubscriptionsAsync(user));
        }

        [Fact]
        public async Task RefreshSubscriptionStates_ExpiresOldPlan_AndStartsScheduledOne()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await SetupAsync(factory);
            await PayAsync(factory, await CheckoutAsync(user, planId));
            await PayAsync(factory, await CheckoutAsync(user, planId));
            await factory.SeedAsync(db =>
            {
                var active = db.UserSubscriptions.Single(s => s.Status == "Active");
                var scheduled = db.UserSubscriptions.Single(s => s.Status == "Scheduled");
                active.StartAt = DateTime.UtcNow.AddDays(-31);
                active.EndAt = DateTime.UtcNow.AddMinutes(-1);
                scheduled.StartAt = active.EndAt.Value;
            });

            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IPaymentService>().RefreshSubscriptionStatesAsync();
            var subscriptions = await GetSubscriptionsAsync(user);

            Assert.Equal(1, subscriptions.Count(s => s.Status == "Active"));
            Assert.Equal(1, subscriptions.Count(s => s.Status == "Expired"));
            Assert.DoesNotContain(subscriptions, s => s.Status == "Scheduled");

            // Gói xếp hàng được bật lên nên không cần quay về gói Miễn phí
            var all = await user.GetFromJsonAsync<List<UserSubscriptionDto>>("/api/user-subscriptions");
            Assert.Equal(1, all!.Count(s => s.Status == "Active"));
        }
    }
}
