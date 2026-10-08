using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Authorize]
    [Route("api/payment-transactions")]
    [ApiController]
    public class PaymentTransactionsController : ControllerBase
    {
        private const string AdminRole = "Admin";

        private readonly IPaymentTransactionService _paymentService;

        public PaymentTransactionsController(IPaymentTransactionService paymentService)
        {
            _paymentService = paymentService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private bool IsAdmin => User.IsInRole(AdminRole);

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<PaymentTransactionDto>>> GetPayments(CancellationToken cancellationToken)
        {
            return Ok(await _paymentService.GetPaymentsAsync(CurrentUserId, IsAdmin, cancellationToken));
        }

        [HttpGet("{paymentId:guid}")]
        public async Task<ActionResult<PaymentTransactionDto>> GetPayment(Guid paymentId, CancellationToken cancellationToken)
        {
            return Ok(await _paymentService.GetPaymentAsync(CurrentUserId, IsAdmin, paymentId, cancellationToken));
        }

        // Giao dịch thường được tạo khi người dùng bấm thanh toán; hiện Admin quản lý thủ công
        [Authorize(Roles = AdminRole)]
        [HttpPost]
        public async Task<ActionResult<PaymentTransactionDto>> CreatePayment(
            [FromBody] PaymentTransactionRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _paymentService.CreatePaymentAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetPayment), new { paymentId = created.Id }, created);
        }

        [Authorize(Roles = AdminRole)]
        [HttpPut("{paymentId:guid}")]
        public async Task<ActionResult<PaymentTransactionDto>> UpdatePayment(
            Guid paymentId,
            [FromBody] PaymentTransactionRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _paymentService.UpdatePaymentAsync(paymentId, request, cancellationToken));
        }

        [Authorize(Roles = AdminRole)]
        [HttpDelete("{paymentId:guid}")]
        public async Task<IActionResult> DeletePayment(Guid paymentId, CancellationToken cancellationToken)
        {
            await _paymentService.DeletePaymentAsync(paymentId, cancellationToken);
            return NoContent();
        }
    }
}
