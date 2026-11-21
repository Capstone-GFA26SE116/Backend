using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    // Lời mời gửi tới tôi. Lời mời chỉ dùng được bởi tài khoản có đúng email được mời (người khác nhận 404).
    [Authorize]
    [Route("api/group-invitations")]
    [ApiController]
    public class GroupInvitationsController : ControllerBase
    {
        private readonly IGroupInvitationService _invitationService;

        public GroupInvitationsController(IGroupInvitationService invitationService)
        {
            _invitationService = invitationService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Lời mời đang chờ (chưa trả lời, chưa hết hạn) gửi tới email của tôi
        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<GroupInvitationDto>>> GetMyInvitations(CancellationToken cancellationToken)
        {
            return Ok(await _invitationService.GetMyInvitationsAsync(CurrentUserId, cancellationToken));
        }

        [HttpPost("{invitationId:guid}/accept")]
        public async Task<ActionResult<GroupDto>> Accept(Guid invitationId, CancellationToken cancellationToken)
        {
            return Ok(await _invitationService.AcceptInvitationAsync(CurrentUserId, invitationId, cancellationToken));
        }

        // Chấp nhận bằng liên kết: Leader gửi AcceptToken nhận được khi tạo lời mời
        [HttpPost("accept-by-token")]
        public async Task<ActionResult<GroupDto>> AcceptByToken([FromBody] GroupInvitationTokenRequestDto request, CancellationToken cancellationToken)
        {
            return Ok(await _invitationService.AcceptInvitationByTokenAsync(CurrentUserId, request.Token, cancellationToken));
        }

        [HttpPost("{invitationId:guid}/decline")]
        public async Task<IActionResult> Decline(Guid invitationId, CancellationToken cancellationToken)
        {
            await _invitationService.DeclineInvitationAsync(CurrentUserId, invitationId, cancellationToken);
            return NoContent();
        }
    }
}
