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
        /// Người dùng duyệt đề xuất lịch (FR-11): chuyển proposal và các slot còn Proposed sang Approved.
        /// Kiểm lại Google Calendar: có slot trùng sự kiện Google thì trả 409 (SCHEDULE_CONFLICT) kèm danh sách và không đổi gì.
        /// skipConflicts = true thì bỏ các slot trùng (Cancelled) và duyệt phần còn lại.
        /// </summary>
        [HttpPost("proposals/{proposalId:guid}/approve")]
        public async Task<ActionResult<ScheduleProposalDto>> ApproveProposal(
            Guid proposalId,
            CancellationToken cancellationToken,
            [FromQuery] bool skipConflicts = false)
        {
            var approved = await _proposalService.ApproveProposalAsync(CurrentUserId, proposalId, skipConflicts, cancellationToken);
            return Ok(approved);
        }

        /// <summary>
        /// Sửa giờ bắt đầu và kết thúc của một slot đề xuất (chỉ khi slot còn Proposed và đề xuất còn Pending).
        /// </summary>
        [HttpPut("proposals/{proposalId:guid}/slots/{slotId:guid}")]
        public async Task<ActionResult<ProposedSlotUpdateResultDto>> UpdateProposedSlot(
            Guid proposalId,
            Guid slotId,
            [FromBody] ProposedSlotUpdateRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _proposalService.UpdateProposedSlotAsync(CurrentUserId, proposalId, slotId, request, cancellationToken));
        }

        /// <summary>
        /// Từ chối một slot đề xuất: slot chuyển sang Cancelled và sẽ không được ghi khi duyệt.
        /// </summary>
        [HttpPost("proposals/{proposalId:guid}/slots/{slotId:guid}/reject")]
        public async Task<ActionResult<ScheduleSlotDto>> RejectProposedSlot(
            Guid proposalId,
            Guid slotId,
            CancellationToken cancellationToken)
        {
            return Ok(await _proposalService.RejectProposedSlotAsync(CurrentUserId, proposalId, slotId, cancellationToken));
        }

        /// <summary>
        /// Xem trước các slot đề xuất đang trùng sự kiện Google (theo dữ liệu đã lưu, không gọi Google).
        /// </summary>
        [HttpGet("proposals/{proposalId:guid}/conflicts")]
        public async Task<ActionResult<ProposalConflictsDto>> GetConflicts(Guid proposalId, CancellationToken cancellationToken)
        {
            return Ok(await _proposalService.GetConflictsAsync(CurrentUserId, proposalId, cancellationToken));
        }
    }
}
