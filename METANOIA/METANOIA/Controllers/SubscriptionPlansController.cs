using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    // Danh mục gói đăng ký: mọi user đăng nhập đều xem được, chỉ Admin được thay đổi
    [Authorize]
    [Route("api/subscription-plans")]
    [ApiController]
    public class SubscriptionPlansController : ControllerBase
    {
        private const string AdminRole = "Admin";

        private readonly ISubscriptionPlanService _planService;

        public SubscriptionPlansController(ISubscriptionPlanService planService)
        {
            _planService = planService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<SubscriptionPlanDto>>> GetPlans(CancellationToken cancellationToken)
        {
            return Ok(await _planService.GetPlansAsync(cancellationToken));
        }

        [HttpGet("{planId:guid}")]
        public async Task<ActionResult<SubscriptionPlanDto>> GetPlan(Guid planId, CancellationToken cancellationToken)
        {
            return Ok(await _planService.GetPlanAsync(planId, cancellationToken));
        }

        [Authorize(Roles = AdminRole)]
        [HttpPost]
        public async Task<ActionResult<SubscriptionPlanDto>> CreatePlan(
            [FromBody] SubscriptionPlanRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _planService.CreatePlanAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetPlan), new { planId = created.Id }, created);
        }

        [Authorize(Roles = AdminRole)]
        [HttpPut("{planId:guid}")]
        public async Task<ActionResult<SubscriptionPlanDto>> UpdatePlan(
            Guid planId,
            [FromBody] SubscriptionPlanRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _planService.UpdatePlanAsync(planId, request, cancellationToken));
        }

        [Authorize(Roles = AdminRole)]
        [HttpDelete("{planId:guid}")]
        public async Task<IActionResult> DeletePlan(Guid planId, CancellationToken cancellationToken)
        {
            await _planService.DeletePlanAsync(planId, cancellationToken);
            return NoContent();
        }
    }
}
