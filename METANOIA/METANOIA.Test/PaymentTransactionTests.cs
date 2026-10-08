using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Test.Infrastructure;
using Xunit;

namespace METANOIA.Test
{
    public class PaymentTransactionTests
    {
        private static async Task<(HttpClient Admin, HttpClient User, Guid UserId, Guid PlanId)> SetupAsync(ApiFactory factory)
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

        private static PaymentTransactionRequestDto NewPayment(Guid userId, Guid planId, long orderCode = 1001) => new()
        {
            UserId = userId,
            PlanId = planId,
            OrderCode = orderCode,
            Gateway = "PayOS",
            Amount = 99000,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        [Fact]
        public async Task Admin_CreatesPayment_DefaultsToPending()
        {
            using var factory = new ApiFactory();
            var (admin, _, userId, planId) = await SetupAsync(factory);

            var response = await admin.PostAsJsonAsync("/api/payment-transactions", NewPayment(userId, planId));
            var payment = await response.Content.ReadFromJsonAsync<PaymentTransactionDto>();

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("Pending", payment!.Status);
        }

        [Fact]
        public async Task User_CannotCreatePayment()
        {
            using var factory = new ApiFactory();
            var (_, user, userId, planId) = await SetupAsync(factory);

            var response = await user.PostAsJsonAsync("/api/payment-transactions", NewPayment(userId, planId));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task DuplicateOrderCode_Returns400()
        {
            using var factory = new ApiFactory();
            var (admin, _, userId, planId) = await SetupAsync(factory);
            await admin.PostAsJsonAsync("/api/payment-transactions", NewPayment(userId, planId));

            var response = await admin.PostAsJsonAsync("/api/payment-transactions", NewPayment(userId, planId));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData("Momo", 99000, "Pending")]
        [InlineData("PayOS", 0, "Pending")]
        [InlineData("PayOS", 99000, "Paid")]
        public async Task InvalidGatewayAmountOrStatus_Returns400(string gateway, long amount, string status)
        {
            using var factory = new ApiFactory();
            var (admin, _, userId, planId) = await SetupAsync(factory);
            var request = NewPayment(userId, planId);
            request.Gateway = gateway;
            request.Amount = amount;
            request.Status = status;

            var response = await admin.PostAsJsonAsync("/api/payment-transactions", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task ExpiresAtInThePast_Returns400()
        {
            using var factory = new ApiFactory();
            var (admin, _, userId, planId) = await SetupAsync(factory);
            var request = NewPayment(userId, planId);
            request.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);

            var response = await admin.PostAsJsonAsync("/api/payment-transactions", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task UserSeesOwnPayments_AndCannotSeeOthers()
        {
            using var factory = new ApiFactory();
            var (admin, user, userId, planId) = await SetupAsync(factory);
            var created = await (await admin.PostAsJsonAsync("/api/payment-transactions", NewPayment(userId, planId)))
                .Content.ReadFromJsonAsync<PaymentTransactionDto>();

            var mine = await user.GetFromJsonAsync<List<PaymentTransactionDto>>("/api/payment-transactions");
            var (stranger, _) = await TestHelpers.LoginAsync(factory, "stranger");
            var strangerView = await stranger.GetAsync($"/api/payment-transactions/{created!.Id}");

            Assert.Single(mine!);
            Assert.Equal(HttpStatusCode.NotFound, strangerView.StatusCode);
        }

        [Fact]
        public async Task Admin_CanUpdateStatusToSucceeded()
        {
            using var factory = new ApiFactory();
            var (admin, _, userId, planId) = await SetupAsync(factory);
            var created = await (await admin.PostAsJsonAsync("/api/payment-transactions", NewPayment(userId, planId)))
                .Content.ReadFromJsonAsync<PaymentTransactionDto>();
            var update = NewPayment(userId, planId);
            update.Status = "Succeeded";
            update.PaidAt = DateTimeOffset.UtcNow;
            update.WebhookReceivedAt = DateTimeOffset.UtcNow;

            var response = await admin.PutAsJsonAsync($"/api/payment-transactions/{created!.Id}", update);
            var updated = await response.Content.ReadFromJsonAsync<PaymentTransactionDto>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Succeeded", updated!.Status);
            Assert.NotNull(updated.PaidAt);
        }

        [Fact]
        public async Task Delete_RemovesPayment_ButNotOneWithInvoice()
        {
            using var factory = new ApiFactory();
            var (admin, _, userId, planId) = await SetupAsync(factory);
            var plain = await (await admin.PostAsJsonAsync("/api/payment-transactions", NewPayment(userId, planId, 1001)))
                .Content.ReadFromJsonAsync<PaymentTransactionDto>();
            var invoiced = await (await admin.PostAsJsonAsync("/api/payment-transactions", NewPayment(userId, planId, 1002)))
                .Content.ReadFromJsonAsync<PaymentTransactionDto>();
            await admin.PostAsJsonAsync("/api/invoices", new InvoiceRequestDto
            {
                PaymentTransactionId = invoiced!.Id,
                InvoiceNumber = "MTN-2026-000010",
                PlanName = "Pro",
                Amount = 99000,
                BillingName = "Nguyễn Văn A",
                BillingEmail = "a@test.com",
                PeriodStart = new DateOnly(2026, 10, 1),
                PeriodEnd = new DateOnly(2026, 10, 31)
            });

            var deletePlain = await admin.DeleteAsync($"/api/payment-transactions/{plain!.Id}");
            var deleteInvoiced = await admin.DeleteAsync($"/api/payment-transactions/{invoiced.Id}");

            Assert.Equal(HttpStatusCode.NoContent, deletePlain.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, deleteInvoiced.StatusCode);
        }
    }
}
