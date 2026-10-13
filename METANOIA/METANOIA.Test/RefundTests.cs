using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Test.Infrastructure;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    public class RefundTests
    {
        private static async Task<(HttpClient Admin, HttpClient User, CheckoutResultDto Checkout, ApiFactory Factory)> PaidAsync(ApiFactory factory)
        {
            var (admin, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);
            await PaymentFlowTests.PayAsync(factory, checkout);
            return (admin, user, checkout, factory);
        }

        private static async Task<RefundDto> RequestAsync(HttpClient user, Guid paymentId, string reason = "Mua nhầm gói")
        {
            var response = await user.PostAsJsonAsync($"/api/payments/{paymentId}/refunds", new RefundRequestDto { Reason = reason });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<RefundDto>())!;
        }

        [Fact]
        public async Task User_CanRequestRefund_ForOwnSucceededPayment()
        {
            using var factory = new ApiFactory();
            var (_, user, checkout, _) = await PaidAsync(factory);

            var refund = await RequestAsync(user, checkout.PaymentId);

            Assert.Equal("Requested", refund.Status);
            Assert.Equal("User", refund.Source);
            Assert.Equal(99000, refund.Amount);
        }

        [Fact]
        public async Task SecondRefundRequest_ForSamePayment_Returns400()
        {
            using var factory = new ApiFactory();
            var (_, user, checkout, _) = await PaidAsync(factory);
            await RequestAsync(user, checkout.PaymentId);

            var response = await user.PostAsJsonAsync($"/api/payments/{checkout.PaymentId}/refunds", new RefundRequestDto { Reason = "Lần nữa" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Refund_ForPendingPayment_Returns400()
        {
            using var factory = new ApiFactory();
            var (_, user, _, planId) = await PaymentFlowTests.SetupAsync(factory);
            var checkout = await PaymentFlowTests.CheckoutAsync(user, planId);

            var response = await user.PostAsJsonAsync($"/api/payments/{checkout.PaymentId}/refunds", new RefundRequestDto { Reason = "Muốn lấy lại tiền" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Refund_WithoutReason_Returns400()
        {
            using var factory = new ApiFactory();
            var (_, user, checkout, _) = await PaidAsync(factory);

            var response = await user.PostAsJsonAsync($"/api/payments/{checkout.PaymentId}/refunds", new RefundRequestDto { Reason = "  " });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Refund_OfOtherUsersPayment_Returns404()
        {
            using var factory = new ApiFactory();
            var (_, _, checkout, _) = await PaidAsync(factory);
            var (stranger, _) = await TestHelpers.LoginAsync(factory, "stranger");

            var response = await stranger.PostAsJsonAsync($"/api/payments/{checkout.PaymentId}/refunds", new RefundRequestDto { Reason = "Không phải của tôi" });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Refund_AfterRefundWindow_Returns400()
        {
            using var factory = new ApiFactory();
            var (_, user, checkout, _) = await PaidAsync(factory);
            await PaymentFlowTests.ModifyPaymentAsync(factory, checkout.PaymentId, p => p.PaidAt = DateTime.UtcNow.AddDays(-8));

            var response = await user.PostAsJsonAsync($"/api/payments/{checkout.PaymentId}/refunds", new RefundRequestDto { Reason = "Quá hạn" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Refund_WhenPlanAlreadyUsed_Returns400()
        {
            using var factory = new ApiFactory();
            var (_, user, checkout, _) = await PaidAsync(factory);
            await factory.SeedAsync(db => db.UserSubscriptions.Single(s => s.PlanId != ApiFactory.FreePlanId).AiCallsUsed = 3);

            var response = await user.PostAsJsonAsync($"/api/payments/{checkout.PaymentId}/refunds", new RefundRequestDto { Reason = "Đã dùng rồi" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Admin_Approve_RevokesSubscription_ThenCompleteWithBankReference()
        {
            using var factory = new ApiFactory();
            var (admin, user, checkout, _) = await PaidAsync(factory);
            var refund = await RequestAsync(user, checkout.PaymentId);

            var approved = await (await admin.PostAsJsonAsync($"/api/refunds/{refund.Id}/approve", new RefundProcessDto { Note = "OK" }))
                .Content.ReadFromJsonAsync<RefundDto>();
            var subscription = Assert.Single(await PaymentFlowTests.GetSubscriptionsAsync(user));
            var completed = await (await admin.PostAsJsonAsync($"/api/refunds/{refund.Id}/complete",
                new RefundProcessDto { BankReference = "FT26281ABC" })).Content.ReadFromJsonAsync<RefundDto>();

            Assert.Equal("Approved", approved!.Status);
            Assert.Equal("Cancelled", subscription.Status);
            Assert.Equal("Completed", completed!.Status);
            Assert.Equal("FT26281ABC", completed.BankReference);
            Assert.NotNull(completed.ProcessedAt);
        }

        [Fact]
        public async Task Complete_BeforeApprove_OrWithoutBankReference_Returns400()
        {
            using var factory = new ApiFactory();
            var (admin, user, checkout, _) = await PaidAsync(factory);
            var refund = await RequestAsync(user, checkout.PaymentId);

            var beforeApprove = await admin.PostAsJsonAsync($"/api/refunds/{refund.Id}/complete", new RefundProcessDto { BankReference = "FT1" });
            await admin.PostAsJsonAsync($"/api/refunds/{refund.Id}/approve", new RefundProcessDto());
            var noReference = await admin.PostAsJsonAsync($"/api/refunds/{refund.Id}/complete", new RefundProcessDto());

            Assert.Equal(HttpStatusCode.BadRequest, beforeApprove.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, noReference.StatusCode);
        }

        [Fact]
        public async Task Reject_NeedsNote_AndAllowsANewRequestAfterwards()
        {
            using var factory = new ApiFactory();
            var (admin, user, checkout, _) = await PaidAsync(factory);
            var refund = await RequestAsync(user, checkout.PaymentId);

            var noNote = await admin.PostAsJsonAsync($"/api/refunds/{refund.Id}/reject", new RefundProcessDto());
            var rejected = await admin.PostAsJsonAsync($"/api/refunds/{refund.Id}/reject", new RefundProcessDto { Note = "Không đủ điều kiện" });
            var again = await user.PostAsJsonAsync($"/api/payments/{checkout.PaymentId}/refunds", new RefundRequestDto { Reason = "Xin xem lại" });

            Assert.Equal(HttpStatusCode.BadRequest, noNote.StatusCode);
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
            Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        }

        [Fact]
        public async Task User_CannotProcessRefunds_AndSeesOnlyOwn()
        {
            using var factory = new ApiFactory();
            var (_, user, checkout, _) = await PaidAsync(factory);
            var refund = await RequestAsync(user, checkout.PaymentId);
            var (stranger, _) = await TestHelpers.LoginAsync(factory, "stranger");

            var approve = await user.PostAsJsonAsync($"/api/refunds/{refund.Id}/approve", new RefundProcessDto());
            var mine = await user.GetFromJsonAsync<List<RefundDto>>("/api/refunds");
            var theirs = await stranger.GetFromJsonAsync<List<RefundDto>>("/api/refunds");
            var peek = await stranger.GetAsync($"/api/refunds/{refund.Id}");

            Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
            Assert.Single(mine!);
            Assert.Empty(theirs!);
            Assert.Equal(HttpStatusCode.NotFound, peek.StatusCode);
        }

        [Fact]
        public async Task Payment_WithRefund_CannotBeDeleted()
        {
            using var factory = new ApiFactory();
            var (admin, user, checkout, _) = await PaidAsync(factory);
            await RequestAsync(user, checkout.PaymentId);

            var response = await admin.DeleteAsync($"/api/payment-transactions/{checkout.PaymentId}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
