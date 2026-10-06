using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Authorize]
    [Route("api/user-subscriptions")]
    [ApiController]
    public class UserSubscriptionsController : ControllerBase
    {
        private const string AdminRole = "Admin";

        private readonly IUserSubscriptionService _subscriptionService;

        public UserSubscriptionsController(IUserSubscriptionService subscriptionService)
        {
            _subscriptionService = subscriptionService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private bool IsAdmin => User.IsInRole(AdminRole);

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<UserSubscriptionDto>>> GetSubscriptions(CancellationToken cancellationToken)
        {
            return Ok(await _subscriptionService.GetSubscriptionsAsync(CurrentUserId, IsAdmin, cancellationToken));
        }

        [HttpGet("{subscriptionId:guid}")]
        public async Task<ActionResult<UserSubscriptionDto>> GetSubscription(Guid subscriptionId, CancellationToken cancellationToken)
        {
            return Ok(await _subscriptionService.GetSubscriptionAsync(CurrentUserId, IsAdmin, subscriptionId, cancellationToken));
        }

        // Gói thường được tạo khi thanh toán thành công; hiện Admin tạo thủ công
        [Authorize(Roles = AdminRole)]
        [HttpPost]
        public async Task<ActionResult<UserSubscriptionDto>> CreateSubscription(
            [FromBody] UserSubscriptionRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _subscriptionService.CreateSubscriptionAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetSubscription), new { subscriptionId = created.Id }, created);
        }

        [Authorize(Roles = AdminRole)]
        [HttpPut("{subscriptionId:guid}")]
        public async Task<ActionResult<UserSubscriptionDto>> UpdateSubscription(
            Guid subscriptionId,
            [FromBody] UserSubscriptionRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _subscriptionService.UpdateSubscriptionAsync(subscriptionId, request, cancellationToken));
        }

        [Authorize(Roles = AdminRole)]
        [HttpDelete("{subscriptionId:guid}")]
        public async Task<IActionResult> DeleteSubscription(Guid subscriptionId, CancellationToken cancellationToken)
        {
            await _subscriptionService.DeleteSubscriptionAsync(subscriptionId, cancellationToken);
            return NoContent();
        }
    }
}
