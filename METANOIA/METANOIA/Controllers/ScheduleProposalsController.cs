using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/schedule")]
    public class ScheduleProposalsController : ControllerBase
    {
        private readonly IScheduleProposalService _proposalService;

        public ScheduleProposalsController(IScheduleProposalService proposalService)
        {
            _proposalService = proposalService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        /// <summary>
        /// Kích hoạt AI đề xuất xếp lịch tối ưu cho các Flexible tasks (FR-10).
        /// </summary>
        [HttpPost("propose")]
        public async Task<ActionResult<AiScheduleProposalResponseDto>> ProposeSchedule(
            [FromBody] ScheduleProposalRequestDto request,
            CancellationToken cancellationToken)
        {
            var result = await _proposalService.ProposeScheduleAsync(CurrentUserId, request, cancellationToken);
            return Ok(result);
        }

        /// <summary>
        /// Lấy bản đề xuất lịch gần nhất đang ở trạng thái chờ duyệt (Pending) nếu có.
        /// </summary>
        [HttpGet("proposals/pending")]
        public async Task<ActionResult<ScheduleProposalDto?>> GetPendingProposal(CancellationToken cancellationToken)
        {
            var proposal = await _proposalService.GetPendingProposalAsync(CurrentUserId, cancellationToken);
            if (proposal is null)
            {
                return NoContent();
            }

            return Ok(proposal);
        }

        /// <summary>
        /// Người dùng duyệt đề xuất lịch (FR-11): chuyển proposal và các slot sang Approved.
        /// </summary>
        [HttpPost("proposals/{proposalId:guid}/approve")]
        public async Task<ActionResult<ScheduleProposalDto>> ApproveProposal(
            Guid proposalId,
            CancellationToken cancellationToken)
        {
            var approved = await _proposalService.ApproveProposalAsync(CurrentUserId, proposalId, cancellationToken);
            return Ok(approved);
        }
    }
}
