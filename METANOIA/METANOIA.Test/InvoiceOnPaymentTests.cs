using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using METANOIA.Test.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // Thanh toán thành công thì tạo đồng thời UserSubscription và Invoice
    public class InvoiceOnPaymentTests
    {
        private static readonly int Year = DateTime.UtcNow.Year;

        private static async Task<List<InvoiceDto>> GetInvoicesAsync(HttpClient client)
        {
            return (await client.GetFromJsonAsync<List<InvoiceDto>>("/api/invoices"))!;
        }

        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static DateOnly LocalDate(DateTimeOffset value)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(value.UtcDateTime, zone));
        }

        [Fact]
        public async Task PaidWebhook_CreatesSubscriptionAndInvoice_Together()
        {
            using var factory = new ApiFactory();
            var (_, user, userId, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);

            // Trước khi trả tiền chưa có gì ngoài giao dịch Pending
            Assert.Empty(await GetInvoicesAsync(user));

            await PaymentFlowTests.PayAsync(factory, checkout);

            var invoice = Assert.Single(await GetInvoicesAsync(user));
            var subscription = Assert.Single(await PaymentFlowTests.GetSubscriptionsAsync(user));
            Assert.Equal(checkout.PaymentId, invoice.PaymentTransactionId);
            Assert.Equal($"MTN-{Year}-000001", invoice.InvoiceNumber);
            Assert.Equal("Pro", invoice.PlanName);
            Assert.Equal(99000, invoice.Amount);
            Assert.Equal("Test user-1", invoice.BillingName);
            Assert.Equal("user-1@test.com", invoice.BillingEmail);
            Assert.Equal(LocalDate(subscription.StartAt), invoice.PeriodStart);
            Assert.Equal(LocalDate(subscription.EndAt!.Value), invoice.PeriodEnd);
            Assert.True(invoice.PeriodEnd > invoice.PeriodStart);
            Assert.Equal(checkout.PaymentId, subscription.PaymentTransactionId);
        }

        [Fact]
        public async Task DuplicateWebhook_DoesNotCreateASecondInvoiceOrSubscription()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);

            await PaymentFlowTests.PayAsync(factory, checkout);
            await PaymentFlowTests.PayAsync(factory, checkout);

            Assert.Single(await GetInvoicesAsync(user));
            Assert.Single(await PaymentFlowTests.GetSubscriptionsAsync(user));
        }

        [Fact]
        public async Task EachPayment_GetsTheNextInvoiceNumber()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);

            await PaymentFlowTests.PayAsync(factory, await PaymentFlowTests.CheckoutAsync(user, planId));
            await PaymentFlowTests.PayAsync(factory, await PaymentFlowTests.CheckoutAsync(user, planId));
            await PaymentFlowTests.PayAsync(factory, await PaymentFlowTests.CheckoutAsync(user, planId));

            var numbers = (await GetInvoicesAsync(user)).Select(i => i.InvoiceNumber).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { $"MTN-{Year}-000001", $"MTN-{Year}-000002", $"MTN-{Year}-000003" }, numbers);
        }

        [Fact]
        public async Task NumbersContinueAfterManualInvoices_AndIgnoreUnparsableOnes()
        {
            using var factory = new ApiFactory();
            var (_, user, userId, planId) = await PaymentFlowTests.SetupAsync(factory);
            await factory.SeedAsync(db =>
            {
                Invoice Manual(string number) => new()
                {
                    Id = Guid.NewGuid(),
                    PaymentTransactionId = Guid.NewGuid(),
                    InvoiceNumber = number,
                    PlanName = "Pro",
                    Amount = 1,
                    BillingName = "x",
                    BillingEmail = "x@x",
                    PeriodStart = new DateOnly(2026, 1, 1),
                    PeriodEnd = new DateOnly(2026, 2, 1),
                    IssuedAt = DateTime.UtcNow
                };
                db.Invoices.AddRange(Manual($"MTN-{Year}-000010"), Manual($"MTN-{Year}-MANUAL"));
            });

            await PaymentFlowTests.PayAsync(factory, await PaymentFlowTests.CheckoutAsync(user, planId));

            var invoice = Assert.Single(await GetInvoicesAsync(user));
            Assert.Equal($"MTN-{Year}-000011", invoice.InvoiceNumber);
        }

        [Fact]
        public async Task PaymentConfirmedBySync_AlsoCreatesTheInvoice()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);
            factory.PayOS.Info[checkout.OrderCode] = factory.PayOS.Info[checkout.OrderCode] with { Status = "PAID", AmountPaid = 99000 };

            await user.PostAsync($"/api/payments/{checkout.PaymentId}/sync", null);

            Assert.Single(await GetInvoicesAsync(user));
            Assert.Single(await PaymentFlowTests.GetSubscriptionsAsync(user));
        }

        [Fact]
        public async Task NoInvoiceIsCreated_WhenThePaymentDidNotSucceed()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var wrongAmount = await PaymentFlowTests.CheckoutAsync(user, planId);
            await PaymentFlowTests.PayAsync(factory, wrongAmount, amount: 50000);
            var cancelled = await PaymentFlowTests.CheckoutAsync(user, planId);
            await user.PostAsync($"/api/payments/{cancelled.PaymentId}/cancel", null);
            await PaymentFlowTests.PayAsync(factory, cancelled);

            Assert.Empty(await GetInvoicesAsync(user));
            Assert.Empty(await PaymentFlowTests.GetSubscriptionsAsync(user));
        }

        [Fact]
        public async Task QueuedPlan_GetsInvoiceForItsOwnPeriod()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            await PaymentFlowTests.PayAsync(factory, await PaymentFlowTests.CheckoutAsync(user, planId));
            var second = await PaymentFlowTests.CheckoutAsync(user, planId);
            await PaymentFlowTests.PayAsync(factory, second);

            var invoices = await GetInvoicesAsync(user);
            var scheduled = (await PaymentFlowTests.GetSubscriptionsAsync(user)).Single(s => s.Status == "Scheduled");
            var invoice = invoices.Single(i => i.PaymentTransactionId == second.PaymentId);

            Assert.Equal(2, invoices.Count);
            Assert.Equal(LocalDate(scheduled.StartAt), invoice.PeriodStart);
            Assert.Equal(LocalDate(scheduled.EndAt!.Value), invoice.PeriodEnd);
        }

        [Fact]
        public async Task OnlyTheOwnerSeesTheInvoice()
        {
            using var factory = new ApiFactory();
            var (admin, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            await PaymentFlowTests.PayAsync(factory, await PaymentFlowTests.CheckoutAsync(user, planId));
            var invoice = (await GetInvoicesAsync(user)).Single();
            var (stranger, _) = await TestHelpers.LoginAsync(factory, "stranger");

            var asStranger = await stranger.GetAsync($"/api/invoices/{invoice.Id}");
            var asAdmin = await admin.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}");

            Assert.Equal(HttpStatusCode.NotFound, asStranger.StatusCode);
            Assert.Equal(invoice.InvoiceNumber, asAdmin!.InvoiceNumber);
            Assert.Empty(await GetInvoicesAsync(stranger));
        }

        // ------------------------------------------------------------ Tự vá khi thiếu

        [Fact]
        public async Task Reconcile_BackfillsMissingInvoice_ForPaymentThatAlreadyHasASubscription()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);
            await PaymentFlowTests.PayAsync(factory, checkout);
            await factory.SeedAsync(db => db.Invoices.RemoveRange(db.Invoices)); // giống giao dịch cũ từ trước khi có hóa đơn tự động

            using var scope = factory.Services.CreateScope();
            var payments = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var ids = await payments.GetPaymentIdsNeedingReconciliationAsync();
            await payments.ReconcilePaymentAsync(checkout.PaymentId);

            Assert.Contains(checkout.PaymentId, ids);
            Assert.Single(await GetInvoicesAsync(user));
            Assert.Single(await PaymentFlowTests.GetSubscriptionsAsync(user)); // không cấp gói thứ hai
        }

        [Fact]
        public async Task Reconcile_CreatesBothWhenSucceededPaymentHasNeither()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);
            await PaymentFlowTests.ModifyPaymentAsync(factory, checkout.PaymentId, p => { p.Status = "Succeeded"; p.PaidAt = DateTime.UtcNow; });

            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IPaymentService>().ReconcilePaymentAsync(checkout.PaymentId);

            Assert.Single(await GetInvoicesAsync(user));
            Assert.Single(await PaymentFlowTests.GetSubscriptionsAsync(user));
        }

        [Fact]
        public async Task SucceededPaymentWithRefund_StillGetsItsInvoice_ButNoNewSubscription()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);
            await PaymentFlowTests.PayAsync(factory, checkout);
            await user.PostAsJsonAsync($"/api/payments/{checkout.PaymentId}/refunds", new RefundRequestDto { Reason = "Mua nhầm" });
            await factory.SeedAsync(db =>
            {
                db.Invoices.RemoveRange(db.Invoices);
                db.UserSubscriptions.RemoveRange(db.UserSubscriptions.Where(s => s.PaymentTransactionId == checkout.PaymentId));
            });

            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IPaymentService>().ReconcilePaymentAsync(checkout.PaymentId);

            Assert.Single(await GetInvoicesAsync(user));
            Assert.Empty(await PaymentFlowTests.GetSubscriptionsAsync(user));
        }

        [Fact]
        public async Task PaymentWithInvoiceAndSubscription_IsLeftAlone()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);
            await PaymentFlowTests.PayAsync(factory, checkout);

            using var scope = factory.Services.CreateScope();
            var ids = await scope.ServiceProvider.GetRequiredService<IPaymentService>().GetPaymentIdsNeedingReconciliationAsync();

            Assert.DoesNotContain(checkout.PaymentId, ids);
        }
    }
}
