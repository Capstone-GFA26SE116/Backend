using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    // Đợt 2 (docs/GROUP_FEATURE.md mục 11): kiểm tra lịch người nhận việc và đề xuất đổi giờ.
    [Authorize]
    [ApiController]
    public class GroupSchedulingController : ControllerBase
    {
        private readonly IGroupAvailabilityService _availability;
        private readonly IGroupChangeRequestService _changeRequests;

        public GroupSchedulingController(IGroupAvailabilityService availability, IGroupChangeRequestService changeRequests)
        {
            _availability = availability;
            _changeRequests = changeRequests;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Leader xem trước trước khi giao: người nhận có bận vào giờ cố định của việc không, và khối lượng việc trong tuần của deadline.
        // Chỉ trả bận hoặc rảnh (khoảng thời gian và nguồn), không có tên dự án hay tiêu đề lịch cá nhân
        [HttpGet("api/group-tasks/{taskId:guid}/assignee-check")]
        public async Task<ActionResult<GroupAssigneeAvailabilityDto>> CheckAssignee(
            Guid taskId,
            [FromQuery] Guid assigneeId,
            CancellationToken cancellationToken)
        {
            return Ok(await _availability.CheckForTaskAsync(CurrentUserId, taskId, assigneeId, cancellationToken));
        }

        // Người được giao việc (hoặc một việc con) đề xuất đổi giờ cố định hoặc deadline kèm lý do. Leader nhận thông báo
        [HttpPost("api/group-tasks/{taskId:guid}/change-requests")]
        public async Task<ActionResult<GroupChangeRequestDto>> CreateChangeRequest(
            Guid taskId,
            [FromBody] GroupChangeRequestCreateDto request,
            CancellationToken cancellationToken)
        {
            var created = await _changeRequests.CreateAsync(CurrentUserId, taskId, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, created);
        }

        [HttpGet("api/group-tasks/{taskId:guid}/change-requests")]
        public async Task<ActionResult<IReadOnlyList<GroupChangeRequestDto>>> GetTaskChangeRequests(Guid taskId, CancellationToken cancellationToken)
        {
            return Ok(await _changeRequests.GetOfTaskAsync(CurrentUserId, taskId, cancellationToken));
        }

        // Leader thấy mọi đề xuất của nhóm, Member chỉ thấy của mình. ?status=Pending để lấy các đề xuất đang chờ
        [HttpGet("api/groups/{groupId:guid}/change-requests")]
        public async Task<ActionResult<IReadOnlyList<GroupChangeRequestDto>>> GetGroupChangeRequests(
            Guid groupId,
            [FromQuery] string? status,
            CancellationToken cancellationToken)
        {
            return Ok(await _changeRequests.GetOfGroupAsync(CurrentUserId, groupId, status, cancellationToken));
        }

        // Leader duyệt: áp dụng giờ mới vào việc. Giờ mới trùng lịch người nhận thì 409 GROUP_ASSIGNEE_BUSY, gửi lại kèm confirmConflicts
        [HttpPost("api/group-change-requests/{requestId:guid}/approve")]
        public async Task<ActionResult<GroupChangeRequestDto>> Approve(
            Guid requestId,
            [FromBody] GroupChangeDecisionDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _changeRequests.ApproveAsync(CurrentUserId, requestId, request, cancellationToken));
        }

        [HttpPost("api/group-change-requests/{requestId:guid}/reject")]
        public async Task<ActionResult<GroupChangeRequestDto>> Reject(
            Guid requestId,
            [FromBody] GroupChangeDecisionDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _changeRequests.RejectAsync(CurrentUserId, requestId, request, cancellationToken));
        }

        // Người đề xuất rút lại đề xuất đang chờ
        [HttpPost("api/group-change-requests/{requestId:guid}/cancel")]
        public async Task<ActionResult<GroupChangeRequestDto>> Cancel(Guid requestId, CancellationToken cancellationToken)
        {
            return Ok(await _changeRequests.CancelAsync(CurrentUserId, requestId, cancellationToken));
        }
    }
}
