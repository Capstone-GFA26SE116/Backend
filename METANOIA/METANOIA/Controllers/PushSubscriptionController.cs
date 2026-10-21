using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace METANOIA.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    [Authorize] // Bắt buộc phải có token JWT
    public class PushSubscriptionController : ControllerBase
    {
        private readonly IPushSubscriptionService _subscriptionService;

        public PushSubscriptionController(IPushSubscriptionService subscriptionService)
        {
            _subscriptionService = subscriptionService;
        }

        [HttpPost("subscribe")]
        public async Task<IActionResult> Subscribe([FromBody] PushSubscriptionDto request)
        {
            if (string.IsNullOrWhiteSpace(request?.Endpoint))
            {
                return BadRequest("Invalid subscription data.");
            }

            var userId = GetUserIdFromToken();

            await _subscriptionService.SubscribeAsync(userId, request);

            return Ok(new { message = "Subscribed to push notifications successfully." });
        }

        [HttpPost("unsubscribe")]
        public async Task<IActionResult> Unsubscribe([FromBody] PushSubscriptionDto request)
        {
            if (string.IsNullOrWhiteSpace(request?.Endpoint))
            {
                return BadRequest("Endpoint is required to unsubscribe.");
            }

            var userId = GetUserIdFromToken();

            await _subscriptionService.UnsubscribeAsync(userId, request.Endpoint);

            return Ok(new { message = "Unsubscribed successfully." });
        }

        // Hàm Helper để trích xuất UserId từ JWT Token
        private Guid GetUserIdFromToken()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                return userId;
            }
            throw new UnauthorizedAccessException("Invalid User Token");
        }
    }
}
