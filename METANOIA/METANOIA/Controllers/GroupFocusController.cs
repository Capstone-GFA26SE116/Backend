using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    // Bấm giờ làm việc trên việc của nhóm (đợt 2, docs/GROUP_FEATURE.md mục 11). Tách khỏi /api/focus của nhánh cá nhân.
    // Chỉ người được giao việc (hoặc việc con) bấm được; giờ làm ghi theo từng người.
    [Authorize]
    [ApiController]
    public class GroupFocusController : ControllerBase
    {
        private readonly IGroupFocusService _focus;

        public GroupFocusController(IGroupFocusService focus)
        {
            _focus = focus;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Việc có việc con thì phải gửi subTaskId. Đang có phiên chạy (nhóm hoặc cá nhân): 409 FOCUS_SESSION_CONFLICT
        [HttpPost("api/group-tasks/{taskId:guid}/focus/start")]
        public async Task<ActionResult<GroupFocusStartResponseDto>> Start(
            Guid taskId,
            [FromBody] GroupFocusStartRequestDto request,
            CancellationToken cancellationToken)
        {
            var started = await _focus.StartAsync(CurrentUserId, taskId, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, started);
        }

        // Dừng với stopReason Completed thì việc con (hoặc việc không có việc con) thành Done và status việc cha tự suy lại
        [HttpPost("api/group-focus/{sessionId:guid}/stop")]
        public async Task<ActionResult<GroupFocusStopResponseDto>> Stop(
            Guid sessionId,
            [FromBody] GroupFocusStopRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _focus.StopAsync(CurrentUserId, sessionId, request, cancellationToken));
        }

        // Phiên đang chạy của tôi trong nhóm; 204 nếu không có
        [HttpGet("api/group-focus/current")]
        public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
        {
            var current = await _focus.GetCurrentAsync(CurrentUserId, cancellationToken);
            return current is null ? NoContent() : Ok(current);
        }

        // Giờ làm thực tế của việc theo từng người và từng việc con, kèm danh sách phiên. Mọi thành viên xem được
        [HttpGet("api/group-tasks/{taskId:guid}/time")]
        public async Task<ActionResult<GroupTaskTimeDto>> GetTime(Guid taskId, CancellationToken cancellationToken)
        {
            return Ok(await _focus.GetTaskTimeAsync(CurrentUserId, taskId, cancellationToken));
        }
    }
}
