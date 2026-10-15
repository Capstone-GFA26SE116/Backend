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
    // Payments:RefundsEnabled = false (mặc định): thanh toán chạy trọn vẹn mà không đụng tới bảng PaymentRefund.
    // Factory dùng repository hoàn tiền "cấm" ném lỗi khi bị gọi, nên chỉ cần test pass là đã chứng minh điều đó.
    public class RefundsDisabledTests
    {
        private static ApiFactory NewFactory() => new() { RefundsEnabled = false, ForbidRefundRepository = true };

        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        // ------------------------------------------------------------ Luồng thanh toán không cần bảng hoàn tiền

        [Fact]
        public async Task PaidWebhook_ActivatesPlanAndCreatesInvoice_WithoutTouchingRefunds()
        {
            using var factory = NewFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);

            var response = await PaymentFlowTests.SendWebhookAsync(factory, FakePayOSClient.WebhookBody(checkout.OrderCode, 99000));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Succeeded", (await PaymentFlowTests.GetPaymentAsync(user, checkout.PaymentId)).Status);
            Assert.Single(await PaymentFlowTests.GetSubscriptionsAsync(user));
            Assert.Single(await user.GetFromJsonAsync<List<InvoiceDto>>("/api/invoices") ?? new());
        }

        [Fact]
        public async Task DuplicateWebhook_StillWorks_WithoutTouchingRefunds()
        {
            using var factory = NewFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);

            await PaymentFlowTests.PayAsync(factory, checkout);
            var again = await PaymentFlowTests.SendWebhookAsync(factory, FakePayOSClient.WebhookBody(checkout.OrderCode, 99000));

            Assert.Contains("Duplicate", await again.Content.ReadAsStringAsync());
            Assert.Single(await PaymentFlowTests.GetSubscriptionsAsync(user));
        }

        [Fact]
        public async Task SyncAndReconciliation_AlsoWork_WithoutTouchingRefunds()
        {
            using var factory = NewFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var viaSync = await PaymentFlowTests.CheckoutAsync(user, planId);
            factory.PayOS.Info[viaSync.OrderCode] = factory.PayOS.Info[viaSync.OrderCode] with { Status = "PAID", AmountPaid = 99000 };

            var sync = await user.PostAsync($"/api/payments/{viaSync.PaymentId}/sync", null);

            Assert.Equal(HttpStatusCode.OK, sync.StatusCode);
            Assert.Single(await PaymentFlowTests.GetSubscriptionsAsync(user));

            // Giao dịch Succeeded thiếu hóa đơn thì worker tự bù, cũng không được hỏi bảng hoàn tiền
            await factory.SeedAsync(db => db.Invoices.RemoveRange(db.Invoices));
            using var scope = factory.Services.CreateScope();
            var payments = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var ids = await payments.GetPaymentIdsNeedingReconciliationAsync();
            await payments.ReconcilePaymentAsync(viaSync.PaymentId);

            Assert.Contains(viaSync.PaymentId, ids);
            Assert.Single(await user.GetFromJsonAsync<List<InvoiceDto>>("/api/invoices") ?? new());
        }

        [Fact]
        public async Task Reconciliation_IgnoresRefundRows_WhenRefundsAreOff_SoAPaidOrderStillGetsItsPlan()
        {
            using var factory = new ApiFactory { RefundsEnabled = false };
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);
            await PaymentFlowTests.ModifyPaymentAsync(factory, checkout.PaymentId, p => { p.Status = "Succeeded"; p.PaidAt = DateTime.UtcNow; });
            await factory.SeedAsync(db => db.PaymentRefunds.Add(new PaymentRefund
            {
                Id = Guid.NewGuid(),
                PaymentTransactionId = checkout.PaymentId,
                Amount = 99000,
                Reason = "Dòng cũ còn sót",
                Source = "System",
                Status = "Requested",
                CreatedAt = DateTime.UtcNow
            }));

            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IPaymentService>().ReconcilePaymentAsync(checkout.PaymentId);

            Assert.Single(await PaymentFlowTests.GetSubscriptionsAsync(user));
        }

        // ------------------------------------------------------------ Tiền về không hợp lệ: không mở yêu cầu hoàn tiền, chỉ báo cần xử lý tay

        [Fact]
        public async Task WrongAmount_MarksFailed_GivesNoPlanOrInvoice_AndReportsReviewRequired()
        {
            using var factory = NewFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);

            var response = await PaymentFlowTests.SendWebhookAsync(factory, FakePayOSClient.WebhookBody(checkout.OrderCode, 50000));
            var payment = await PaymentFlowTests.GetPaymentAsync(user, checkout.PaymentId);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("ReviewRequired", await response.Content.ReadAsStringAsync());
            Assert.Equal("Failed", payment.Status);
            Assert.NotNull(payment.WebhookReceivedAt);
            Assert.Empty(await PaymentFlowTests.GetSubscriptionsAsync(user));
            Assert.Empty(await user.GetFromJsonAsync<List<InvoiceDto>>("/api/invoices") ?? new());
            Assert.Equal(0, await QueryAsync(factory, db => db.PaymentRefunds.CountAsync()));
        }

        [Fact]
        public async Task MoneyAfterCancel_KeepsCancelled_RecordsTheWebhookTime_AndCreatesNothing()
        {
            using var factory = NewFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);
            await user.PostAsync($"/api/payments/{checkout.PaymentId}/cancel", null);

            var response = await PaymentFlowTests.SendWebhookAsync(factory, FakePayOSClient.WebhookBody(checkout.OrderCode, 99000));
            var payment = await PaymentFlowTests.GetPaymentAsync(user, checkout.PaymentId);

            Assert.Contains("ReviewRequired", await response.Content.ReadAsStringAsync());
            Assert.Equal("Cancelled", payment.Status);
            Assert.NotNull(payment.WebhookReceivedAt); // dấu hiệu "đã nhận tiền sau khi hủy" để admin tìm
            Assert.Empty(await PaymentFlowTests.GetSubscriptionsAsync(user));
            Assert.Equal(0, await QueryAsync(factory, db => db.PaymentRefunds.CountAsync()));
        }

        [Fact]
        public async Task LatePaymentOnExpiredOrder_IsStillAccepted()
        {
            using var factory = NewFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);
            await PaymentFlowTests.ModifyPaymentAsync(factory, checkout.PaymentId, p => p.Status = "Expired");

            await PaymentFlowTests.PayAsync(factory, checkout);

            Assert.Equal("Succeeded", (await PaymentFlowTests.GetPaymentAsync(user, checkout.PaymentId)).Status);
            Assert.Single(await PaymentFlowTests.GetSubscriptionsAsync(user));
        }

        // ------------------------------------------------------------ Chức năng hoàn tiền bị khóa đến khi bật

        [Fact]
        public async Task RefundEndpoints_AreRefusedWhileRefundsAreOff()
        {
            using var factory = NewFactory();
            var (admin, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);
            await PaymentFlowTests.PayAsync(factory, checkout);

            var request = await user.PostAsJsonAsync($"/api/payments/{checkout.PaymentId}/refunds", new RefundRequestDto { Reason = "Mua nhầm" });
            var list = await admin.GetAsync("/api/refunds");
            var approve = await admin.PostAsJsonAsync($"/api/refunds/{Guid.NewGuid()}/approve", new RefundProcessDto());

            Assert.Equal(HttpStatusCode.BadRequest, request.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, list.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);
            Assert.Contains("chưa được bật", await request.Content.ReadAsStringAsync() is { } body ? System.Text.RegularExpressions.Regex.Unescape(body) : "");
        }

        [Fact]
        public async Task DeletingAPaymentIsNotBlockedByRefundRows_WhenRefundsAreOff()
        {
            using var factory = NewFactory();
            var (admin, user, userId, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);

            var response = await admin.DeleteAsync($"/api/payment-transactions/{checkout.PaymentId}");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.NotEqual(Guid.Empty, userId);
        }
    }
}
