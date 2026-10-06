using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Authorize]
    [ApiController]
    public class MilestonesController : ControllerBase
    {
        private readonly IMilestoneService _milestoneService;

        public MilestonesController(IMilestoneService milestoneService)
        {
            _milestoneService = milestoneService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet("api/projects/{projectId:guid}/milestones")]
        public async Task<ActionResult<IReadOnlyList<MilestoneDto>>> GetMilestones(Guid projectId, CancellationToken cancellationToken)
        {
            return Ok(await _milestoneService.GetMilestonesAsync(CurrentUserId, projectId, cancellationToken));
        }

        [HttpPost("api/projects/{projectId:guid}/milestones")]
        public async Task<ActionResult<MilestoneDto>> CreateMilestone(
            Guid projectId,
            [FromBody] MilestoneRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _milestoneService.CreateMilestoneAsync(CurrentUserId, projectId, request, cancellationToken);
            return CreatedAtAction(nameof(GetMilestone), new { milestoneId = created.Id }, created);
        }

        [HttpGet("api/milestones/{milestoneId:guid}")]
        public async Task<ActionResult<MilestoneDto>> GetMilestone(Guid milestoneId, CancellationToken cancellationToken)
        {
            return Ok(await _milestoneService.GetMilestoneAsync(CurrentUserId, milestoneId, cancellationToken));
        }

        [HttpPut("api/milestones/{milestoneId:guid}")]
        public async Task<ActionResult<MilestoneDto>> UpdateMilestone(
            Guid milestoneId,
            [FromBody] MilestoneRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _milestoneService.UpdateMilestoneAsync(CurrentUserId, milestoneId, request, cancellationToken));
        }

        [HttpDelete("api/milestones/{milestoneId:guid}")]
        public async Task<IActionResult> DeleteMilestone(Guid milestoneId, CancellationToken cancellationToken)
        {
            await _milestoneService.DeleteMilestoneAsync(CurrentUserId, milestoneId, cancellationToken);
            return NoContent();
        }
    }
}
