using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    // Nhánh Group (làm việc nhóm): nhóm, thành viên, lời mời của nhóm, nhật ký, tiến độ. Xem docs/GROUP_FEATURE.md.
    // Người không thuộc nhóm nhận 404; người thuộc nhóm nhưng thiếu quyền nhận 403.
    [Authorize]
    [Route("api/groups")]
    [ApiController]
    public class GroupsController : ControllerBase
    {
        private readonly IGroupService _groupService;
        private readonly IGroupInvitationService _invitationService;
        private readonly IGroupProgressService _progressService;

        public GroupsController(
            IGroupService groupService,
            IGroupInvitationService invitationService,
            IGroupProgressService progressService)
        {
            _groupService = groupService;
            _invitationService = invitationService;
            _progressService = progressService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Nhóm của tôi (mọi vai trò). Nhóm đã lưu trữ chỉ hiện khi includeArchived = true
        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<GroupDto>>> GetMyGroups([FromQuery] bool includeArchived, CancellationToken cancellationToken)
        {
            return Ok(await _groupService.GetMyGroupsAsync(CurrentUserId, includeArchived, cancellationToken));
        }

        // Người tạo thành Leader đầu tiên của nhóm
        [HttpPost]
        public async Task<ActionResult<GroupDto>> CreateGroup([FromBody] GroupRequestDto request, CancellationToken cancellationToken)
        {
            var created = await _groupService.CreateGroupAsync(CurrentUserId, request, cancellationToken);
            return CreatedAtAction(nameof(GetGroup), new { groupId = created.Id }, created);
        }

        [HttpGet("{groupId:guid}")]
        public async Task<ActionResult<GroupDto>> GetGroup(Guid groupId, CancellationToken cancellationToken)
        {
            return Ok(await _groupService.GetGroupAsync(CurrentUserId, groupId, cancellationToken));
        }

        [HttpPut("{groupId:guid}")]
        public async Task<ActionResult<GroupDto>> UpdateGroup(Guid groupId, [FromBody] GroupRequestDto request, CancellationToken cancellationToken)
        {
            return Ok(await _groupService.UpdateGroupAsync(CurrentUserId, groupId, request, cancellationToken));
        }

        // Lưu trữ: nhóm chỉ còn xem được. Chỉ Leader
        [HttpPost("{groupId:guid}/archive")]
        public async Task<ActionResult<GroupDto>> ArchiveGroup(Guid groupId, CancellationToken cancellationToken)
        {
            return Ok(await _groupService.ArchiveGroupAsync(CurrentUserId, groupId, cancellationToken));
        }

        [HttpPost("{groupId:guid}/restore")]
        public async Task<ActionResult<GroupDto>> RestoreGroup(Guid groupId, CancellationToken cancellationToken)
        {
            return Ok(await _groupService.RestoreGroupAsync(CurrentUserId, groupId, cancellationToken));
        }

        // ---------------------------------------------------------------- Thành viên

        [HttpGet("{groupId:guid}/members")]
        public async Task<ActionResult<IReadOnlyList<GroupMemberDto>>> GetMembers(Guid groupId, CancellationToken cancellationToken)
        {
            return Ok(await _groupService.GetMembersAsync(CurrentUserId, groupId, cancellationToken));
        }

        // Đổi vai trò (Leader). Nhóm luôn phải còn ít nhất một Leader
        [HttpPut("{groupId:guid}/members/{userId:guid}")]
        public async Task<ActionResult<GroupMemberDto>> ChangeMemberRole(
            Guid groupId,
            Guid userId,
            [FromBody] GroupMemberRoleRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _groupService.ChangeMemberRoleAsync(CurrentUserId, groupId, userId, request, cancellationToken));
        }

        // Leader xóa thành viên; việc đang giao cho người đó về chưa giao
        [HttpDelete("{groupId:guid}/members/{userId:guid}")]
        public async Task<IActionResult> RemoveMember(Guid groupId, Guid userId, CancellationToken cancellationToken)
        {
            await _groupService.RemoveMemberAsync(CurrentUserId, groupId, userId, cancellationToken);
            return NoContent();
        }

        // Tự rời nhóm (mọi vai trò, trừ Leader cuối)
        [HttpPost("{groupId:guid}/leave")]
        public async Task<IActionResult> LeaveGroup(Guid groupId, CancellationToken cancellationToken)
        {
            await _groupService.LeaveGroupAsync(CurrentUserId, groupId, cancellationToken);
            return NoContent();
        }

        // ---------------------------------------------------------------- Lời mời của nhóm (Leader)

        // Trả AcceptToken đúng một lần để Leader gửi liên kết cho người được mời
        [HttpPost("{groupId:guid}/invitations")]
        public async Task<ActionResult<GroupInvitationCreatedDto>> CreateInvitation(
            Guid groupId,
            [FromBody] GroupInvitationRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _invitationService.CreateInvitationAsync(CurrentUserId, groupId, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, created);
        }

        [HttpGet("{groupId:guid}/invitations")]
        public async Task<ActionResult<IReadOnlyList<GroupInvitationDto>>> GetInvitations(Guid groupId, CancellationToken cancellationToken)
        {
            return Ok(await _invitationService.GetInvitationsOfGroupAsync(CurrentUserId, groupId, cancellationToken));
        }

        [HttpDelete("{groupId:guid}/invitations/{invitationId:guid}")]
        public async Task<IActionResult> RevokeInvitation(Guid groupId, Guid invitationId, CancellationToken cancellationToken)
        {
            await _invitationService.RevokeInvitationAsync(CurrentUserId, groupId, invitationId, cancellationToken);
            return NoContent();
        }

        // ---------------------------------------------------------------- Nhật ký và tiến độ

        // Nhật ký hoạt động, mới nhất trước. Leader và Member xem được
        [HttpGet("{groupId:guid}/activity")]
        public async Task<ActionResult<IReadOnlyList<GroupActivityDto>>> GetActivity(
            Guid groupId,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 50,
            CancellationToken cancellationToken = default)
        {
            return Ok(await _groupService.GetActivityAsync(CurrentUserId, groupId, skip, take, cancellationToken));
        }

        // Số việc theo status, % hoàn thành, tiến độ từng thành viên và từng giai đoạn
        [HttpGet("{groupId:guid}/progress")]
        public async Task<ActionResult<GroupProgressDto>> GetProgress(Guid groupId, CancellationToken cancellationToken)
        {
            return Ok(await _progressService.GetProgressAsync(CurrentUserId, groupId, cancellationToken));
        }
    }
}
