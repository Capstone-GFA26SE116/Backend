using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Authorize]
    [Route("api/payments")]
    [ApiController]
    public class PaymentsController : ControllerBase
    {
        private const string AdminRole = "Admin";

        private readonly IPaymentService _paymentService;
        private readonly IRefundService _refundService;

        public PaymentsController(IPaymentService paymentService, IRefundService refundService)
        {
            _paymentService = paymentService;
            _refundService = refundService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private bool IsAdmin => User.IsInRole(AdminRole);

        // Tạo giao dịch Pending + link thanh toán PayOS. Bấm lại khi link cũ còn sống sẽ nhận lại link cũ (Reused = true)
        [HttpPost("checkout")]
        public async Task<ActionResult<CheckoutResultDto>> Checkout([FromBody] CheckoutRequestDto request, CancellationToken cancellationToken)
        {
            return Ok(await _paymentService.CheckoutAsync(CurrentUserId, request.PlanId, cancellationToken));
        }

        // Frontend gọi khi PayOS đưa người dùng về returnUrl/cancelUrl, hoặc khi nghi ngờ webhook bị lỡ.
        // Đừng tin tham số trên URL return: luôn gọi endpoint này để lấy trạng thái thật từ PayOS.
        [HttpPost("{paymentId:guid}/sync")]
        public async Task<ActionResult<PaymentTransactionDto>> Sync(Guid paymentId, CancellationToken cancellationToken)
        {
            return Ok(await _paymentService.SyncAsync(CurrentUserId, IsAdmin, paymentId, cancellationToken));
        }

        [HttpPost("{paymentId:guid}/cancel")]
        public async Task<ActionResult<PaymentTransactionDto>> Cancel(Guid paymentId, CancellationToken cancellationToken)
        {
            return Ok(await _paymentService.CancelAsync(CurrentUserId, IsAdmin, paymentId, cancellationToken));
        }

        [HttpPost("{paymentId:guid}/refunds")]
        public async Task<ActionResult<RefundDto>> RequestRefund(
            Guid paymentId,
            [FromBody] RefundRequestDto request,
            CancellationToken cancellationToken)
        {
            var refund = await _refundService.RequestRefundAsync(CurrentUserId, paymentId, request, cancellationToken);
            return CreatedAtAction(nameof(RefundsController.GetRefund), "Refunds", new { refundId = refund.Id }, refund);
        }
    }
}
