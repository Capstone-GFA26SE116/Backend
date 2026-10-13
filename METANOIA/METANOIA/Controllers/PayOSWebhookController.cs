using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Route("api/payos")]
    [ApiController]
    public class PayOSWebhookController : ControllerBase
    {
        private const string AdminRole = "Admin";

        private readonly IPaymentService _paymentService;
        private readonly ILogger<PayOSWebhookController> _logger;

        public PayOSWebhookController(IPaymentService paymentService, ILogger<PayOSWebhookController> logger)
        {
            _paymentService = paymentService;
            _logger = logger;
        }

        // PayOS gọi endpoint này (không có JWT); bảo mật bằng chữ ký HMAC với ChecksumKey.
        // Trả 200 cho mọi webhook hợp lệ (kể cả trùng/không liên quan) để PayOS ngừng gửi lại;
        // lỗi hệ thống (500/503) khiến PayOS gửi lại sau, và xử lý là idempotent nên gửi lại không gây hại.
        [AllowAnonymous]
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(Request.Body);
            var rawBody = await reader.ReadToEndAsync(cancellationToken);

            var outcome = await _paymentService.HandleWebhookAsync(rawBody, cancellationToken);
            if (outcome == WebhookOutcome.InvalidSignature)
            {
                return BadRequest(new { success = false, message = "Chữ ký webhook không hợp lệ." });
            }

            _logger.LogInformation("Webhook PayOS xử lý xong: {Outcome}", outcome);
            return Ok(new { success = true, outcome = outcome.ToString() });
        }

        // Admin gọi một lần để đăng ký PayOS:WebhookUrl với PayOS (cũng có thể làm trên dashboard my.payos.vn)
        [Authorize(Roles = AdminRole)]
        [HttpPost("confirm-webhook")]
        public async Task<IActionResult> ConfirmWebhook(CancellationToken cancellationToken)
        {
            await _paymentService.ConfirmWebhookAsync(cancellationToken);
            return Ok(new { success = true, message = "Đã đăng ký webhook với PayOS." });
        }
    }
}
