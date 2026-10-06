using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Test.Infrastructure;
using Xunit;
using DomainEntities = METANOIA.Domain.Entities;

namespace METANOIA.Test
{
    public class SubscriptionTests
    {
        private static SubscriptionPlanRequestDto NewPlan(string name = "Pro") => new()
        {
            Name = name,
            Price = 99000,
            DurationDays = 30,
            AiCallLimit = 500,
            StorageLimitMb = 2048,
            ProjectLimit = null
        };

        private static async Task<(HttpClient Admin, HttpClient User, Guid UserId, Guid PlanId)> SetupAsync(ApiFactory factory)
        {
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, _) = await TestHelpers.LoginAsync(factory, "admin-1");
            var (user, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            var plan = await (await admin.PostAsJsonAsync("/api/subscription-plans", NewPlan()))
                .Content.ReadFromJsonAsync<SubscriptionPlanDto>();
            return (admin, user, auth.UserId, plan!.Id);
        }

        private static async Task<Guid> SeedPaymentAsync(ApiFactory factory, Guid userId, Guid planId, long amount)
        {
            var paymentId = Guid.NewGuid();
            await factory.SeedAsync(db => db.PaymentTransactions.Add(new DomainEntities.PaymentTransaction
            {
                Id = paymentId,
                UserId = userId,
                PlanId = planId,
                OrderCode = Math.Abs(paymentId.GetHashCode()) + 1L,
                Gateway = "PayOS",
                Amount = amount,
                Status = "Paid",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                PaidAt = DateTime.UtcNow
            }));
            return paymentId;
        }

        [Fact]
        public async Task Admin_CanCreatePlan_UserCannot()
        {
            using var factory = new ApiFactory();
            var (admin, user, _, _) = await SetupAsync(factory);

            var userAttempt = await user.PostAsJsonAsync("/api/subscription-plans", NewPlan("Basic"));
            var plans = await user.GetFromJsonAsync<List<SubscriptionPlanDto>>("/api/subscription-plans");

            Assert.Equal(HttpStatusCode.Forbidden, userAttempt.StatusCode);
            Assert.Contains(plans!, p => p.Name == "Pro");
        }

        [Fact]
        public async Task PlanWithDuplicateName_Returns400()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, _) = await SetupAsync(factory);

            var response = await admin.PostAsJsonAsync("/api/subscription-plans", NewPlan("PRO"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task SecondActiveSubscription_ForSameUser_Returns400()
        {
            using var factory = new ApiFactory();
            var (admin, _, userId, planId) = await SetupAsync(factory);
            var first = new UserSubscriptionRequestDto { UserId = userId, PlanId = planId, StartAt = DateTimeOffset.UtcNow, Status = "Active" };
            await admin.PostAsJsonAsync("/api/user-subscriptions", first);

            var response = await admin.PostAsJsonAsync("/api/user-subscriptions", first);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task UserSeesOwnSubscriptions_AndCannotSeeOthers()
        {
            using var factory = new ApiFactory();
            var (admin, user, userId, planId) = await SetupAsync(factory);
            var created = await (await admin.PostAsJsonAsync("/api/user-subscriptions",
                new UserSubscriptionRequestDto { UserId = userId, PlanId = planId, StartAt = DateTimeOffset.UtcNow }))
                .Content.ReadFromJsonAsync<UserSubscriptionDto>();

            var mine = await user.GetFromJsonAsync<List<UserSubscriptionDto>>("/api/user-subscriptions");
            var (stranger, _) = await TestHelpers.LoginAsync(factory, "stranger");
            var strangerView = await stranger.GetAsync($"/api/user-subscriptions/{created!.Id}");

            Assert.Single(mine!);
            Assert.Equal(HttpStatusCode.NotFound, strangerView.StatusCode);
        }

        [Fact]
        public async Task EndBeforeStart_Returns400()
        {
            using var factory = new ApiFactory();
            var (admin, _, userId, planId) = await SetupAsync(factory);
            var start = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

            var response = await admin.PostAsJsonAsync("/api/user-subscriptions", new UserSubscriptionRequestDto
            {
                UserId = userId,
                PlanId = planId,
                StartAt = start,
                EndAt = start.AddDays(-1)
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Invoice_WithMatchingAmount_IsCreated_AndVisibleToOwner()
        {
            using var factory = new ApiFactory();
            var (admin, user, userId, planId) = await SetupAsync(factory);
            var paymentId = await SeedPaymentAsync(factory, userId, planId, 99000);

            var response = await admin.PostAsJsonAsync("/api/invoices", new InvoiceRequestDto
            {
                PaymentTransactionId = paymentId,
                InvoiceNumber = "MTN-2026-000001",
                PlanName = "Pro",
                Amount = 99000,
                BillingName = "Nguyễn Văn A",
                BillingEmail = "a@test.com",
                PeriodStart = new DateOnly(2026, 10, 1),
                PeriodEnd = new DateOnly(2026, 10, 31)
            });
            var invoice = await response.Content.ReadFromJsonAsync<InvoiceDto>();
            var mine = await user.GetFromJsonAsync<List<InvoiceDto>>("/api/invoices");

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Contains(mine!, i => i.Id == invoice!.Id);
        }

        [Fact]
        public async Task Invoice_WithAmountMismatch_Returns400()
        {
            using var factory = new ApiFactory();
            var (admin, _, userId, planId) = await SetupAsync(factory);
            var paymentId = await SeedPaymentAsync(factory, userId, planId, 99000);

            var response = await admin.PostAsJsonAsync("/api/invoices", new InvoiceRequestDto
            {
                PaymentTransactionId = paymentId,
                InvoiceNumber = "MTN-2026-000002",
                PlanName = "Pro",
                Amount = 50000,
                BillingName = "Nguyễn Văn A",
                BillingEmail = "a@test.com",
                PeriodStart = new DateOnly(2026, 10, 1),
                PeriodEnd = new DateOnly(2026, 10, 31)
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task OtherUsersInvoice_Returns404()
        {
            using var factory = new ApiFactory();
            var (admin, _, userId, planId) = await SetupAsync(factory);
            var paymentId = await SeedPaymentAsync(factory, userId, planId, 99000);
            var invoice = await (await admin.PostAsJsonAsync("/api/invoices", new InvoiceRequestDto
            {
                PaymentTransactionId = paymentId,
                InvoiceNumber = "MTN-2026-000003",
                PlanName = "Pro",
                Amount = 99000,
                BillingName = "Nguyễn Văn A",
                BillingEmail = "a@test.com",
                PeriodStart = new DateOnly(2026, 10, 1),
                PeriodEnd = new DateOnly(2026, 10, 31)
            })).Content.ReadFromJsonAsync<InvoiceDto>();

            var (stranger, _) = await TestHelpers.LoginAsync(factory, "stranger");
            var response = await stranger.GetAsync($"/api/invoices/{invoice!.Id}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
