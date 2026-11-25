using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    // Giai đoạn, việc, việc con, bình luận của nhóm và "việc của tôi". Xem docs/GROUP_FEATURE.md mục 4, 5 và 7.
    // Leader tạo, sửa, xóa và giao việc; Member đổi status việc được giao cho mình và bình luận; Viewer chỉ xem.
    [Authorize]
    [ApiController]
    public class GroupWorkController : ControllerBase
    {
        private readonly IGroupMilestoneService _milestones;
        private readonly IGroupTaskService _tasks;
        private readonly IGroupCommentService _comments;
        private readonly IGroupProgressService _progress;
        private readonly IGroupScheduleService _schedule;

        public GroupWorkController(
            IGroupMilestoneService milestones,
            IGroupTaskService tasks,
            IGroupCommentService comments,
            IGroupProgressService progress,
            IGroupScheduleService schedule)
        {
            _milestones = milestones;
            _tasks = tasks;
            _comments = comments;
            _progress = progress;
            _schedule = schedule;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ================================================================ Giai đoạn

        [HttpGet("api/groups/{groupId:guid}/milestones")]
        public async Task<ActionResult<IReadOnlyList<GroupMilestoneDto>>> GetMilestones(Guid groupId, CancellationToken cancellationToken)
        {
            return Ok(await _milestones.GetMilestonesAsync(CurrentUserId, groupId, cancellationToken));
        }

        [HttpPost("api/groups/{groupId:guid}/milestones")]
        public async Task<ActionResult<GroupMilestoneDto>> CreateMilestone(
            Guid groupId,
            [FromBody] GroupMilestoneRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _milestones.CreateMilestoneAsync(CurrentUserId, groupId, request, cancellationToken);
            return CreatedAtAction(nameof(GetMilestone), new { milestoneId = created.Id }, created);
        }

        [HttpGet("api/group-milestones/{milestoneId:guid}")]
        public async Task<ActionResult<GroupMilestoneDto>> GetMilestone(Guid milestoneId, CancellationToken cancellationToken)
        {
            return Ok(await _milestones.GetMilestoneAsync(CurrentUserId, milestoneId, cancellationToken));
        }

        // Chuyển sang Delivered khi còn việc chưa xong trả 409 (MILESTONE_HAS_OPEN_TASKS); gửi lại kèm confirmOpenTasks = true để vẫn giao
        [HttpPut("api/group-milestones/{milestoneId:guid}")]
        public async Task<ActionResult<GroupMilestoneDto>> UpdateMilestone(
            Guid milestoneId,
            [FromBody] GroupMilestoneRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _milestones.UpdateMilestoneAsync(CurrentUserId, milestoneId, request, cancellationToken));
        }

        [HttpDelete("api/group-milestones/{milestoneId:guid}")]
        public async Task<IActionResult> DeleteMilestone(Guid milestoneId, CancellationToken cancellationToken)
        {
            await _milestones.DeleteMilestoneAsync(CurrentUserId, milestoneId, cancellationToken);
            return NoContent();
        }

        // ================================================================ Việc

        [HttpGet("api/groups/{groupId:guid}/tasks")]
        public async Task<ActionResult<IReadOnlyList<GroupTaskDto>>> GetTasks(
            Guid groupId,
            [FromQuery] string? status,
            [FromQuery] Guid? assigneeId,
            [FromQuery] Guid? milestoneId,
            CancellationToken cancellationToken)
        {
            return Ok(await _tasks.GetTasksAsync(CurrentUserId, groupId, status, assigneeId, milestoneId, cancellationToken));
        }

        [HttpPost("api/groups/{groupId:guid}/tasks")]
        public async Task<ActionResult<GroupTaskDto>> CreateTask(
            Guid groupId,
            [FromBody] GroupTaskRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _tasks.CreateTaskAsync(CurrentUserId, groupId, request, cancellationToken);
            return CreatedAtAction(nameof(GetTask), new { taskId = created.Id }, created);
        }

        [HttpGet("api/group-tasks/{taskId:guid}")]
        public async Task<ActionResult<GroupTaskDto>> GetTask(Guid taskId, CancellationToken cancellationToken)
        {
            return Ok(await _tasks.GetTaskAsync(CurrentUserId, taskId, cancellationToken));
        }

        // Sửa thông tin việc (Leader). Status và người được giao đổi qua hai API riêng bên dưới
        [HttpPut("api/group-tasks/{taskId:guid}")]
        public async Task<ActionResult<GroupTaskDto>> UpdateTask(
            Guid taskId,
            [FromBody] GroupTaskRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _tasks.UpdateTaskAsync(CurrentUserId, taskId, request, cancellationToken));
        }

        [HttpDelete("api/group-tasks/{taskId:guid}")]
        public async Task<IActionResult> DeleteTask(Guid taskId, CancellationToken cancellationToken)
        {
            await _tasks.DeleteTaskAsync(CurrentUserId, taskId, cancellationToken);
            return NoContent();
        }

        // Leader đổi mọi việc; Member chỉ đổi việc được giao cho mình và không có việc con
        [HttpPut("api/group-tasks/{taskId:guid}/status")]
        public async Task<ActionResult<GroupTaskDto>> ChangeTaskStatus(
            Guid taskId,
            [FromBody] GroupTaskStatusRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _tasks.ChangeTaskStatusAsync(CurrentUserId, taskId, request, cancellationToken));
        }

        // Giao việc (Leader). AssigneeId rỗng = bỏ giao việc. Chỉ giao cho Leader hoặc Member của nhóm
        [HttpPut("api/group-tasks/{taskId:guid}/assignee")]
        public async Task<ActionResult<GroupTaskDto>> AssignTask(
            Guid taskId,
            [FromBody] GroupAssigneeRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _tasks.AssignTaskAsync(CurrentUserId, taskId, request, cancellationToken));
        }

        // ================================================================ Việc con

        [HttpGet("api/group-tasks/{taskId:guid}/subtasks")]
        public async Task<ActionResult<IReadOnlyList<GroupSubTaskDto>>> GetSubTasks(Guid taskId, CancellationToken cancellationToken)
        {
            return Ok(await _tasks.GetSubTasksAsync(CurrentUserId, taskId, cancellationToken));
        }

        [HttpPost("api/group-tasks/{taskId:guid}/subtasks")]
        public async Task<ActionResult<GroupSubTaskDto>> CreateSubTask(
            Guid taskId,
            [FromBody] GroupSubTaskRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _tasks.CreateSubTaskAsync(CurrentUserId, taskId, request, cancellationToken);
            return CreatedAtAction(nameof(GetSubTask), new { subTaskId = created.Id }, created);
        }

        [HttpGet("api/group-subtasks/{subTaskId:guid}")]
        public async Task<ActionResult<GroupSubTaskDto>> GetSubTask(Guid subTaskId, CancellationToken cancellationToken)
        {
            return Ok(await _tasks.GetSubTaskAsync(CurrentUserId, subTaskId, cancellationToken));
        }

        [HttpPut("api/group-subtasks/{subTaskId:guid}")]
        public async Task<ActionResult<GroupSubTaskDto>> UpdateSubTask(
            Guid subTaskId,
            [FromBody] GroupSubTaskRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _tasks.UpdateSubTaskAsync(CurrentUserId, subTaskId, request, cancellationToken));
        }

        [HttpDelete("api/group-subtasks/{subTaskId:guid}")]
        public async Task<IActionResult> DeleteSubTask(Guid subTaskId, CancellationToken cancellationToken)
        {
            await _tasks.DeleteSubTaskAsync(CurrentUserId, subTaskId, cancellationToken);
            return NoContent();
        }

        // Leader đổi mọi việc con; Member đổi việc con được giao cho mình. Status của việc cha tự suy lại
        [HttpPut("api/group-subtasks/{subTaskId:guid}/status")]
        public async Task<ActionResult<GroupSubTaskDto>> ChangeSubTaskStatus(
            Guid subTaskId,
            [FromBody] GroupTaskStatusRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _tasks.ChangeSubTaskStatusAsync(CurrentUserId, subTaskId, request, cancellationToken));
        }

        [HttpPut("api/group-subtasks/{subTaskId:guid}/assignee")]
        public async Task<ActionResult<GroupSubTaskDto>> AssignSubTask(
            Guid subTaskId,
            [FromBody] GroupAssigneeRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _tasks.AssignSubTaskAsync(CurrentUserId, subTaskId, request, cancellationToken));
        }

        // ================================================================ Bình luận

        [HttpGet("api/group-tasks/{taskId:guid}/comments")]
        public async Task<ActionResult<IReadOnlyList<GroupCommentDto>>> GetComments(Guid taskId, CancellationToken cancellationToken)
        {
            return Ok(await _comments.GetCommentsAsync(CurrentUserId, taskId, cancellationToken));
        }

        // Nhắc tên bằng @email của thành viên trong nội dung để họ nhận thông báo
        [HttpPost("api/group-tasks/{taskId:guid}/comments")]
        public async Task<ActionResult<GroupCommentDto>> CreateComment(
            Guid taskId,
            [FromBody] GroupCommentRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _comments.CreateCommentAsync(CurrentUserId, taskId, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, created);
        }

        [HttpPut("api/group-comments/{commentId:guid}")]
        public async Task<ActionResult<GroupCommentDto>> UpdateComment(
            Guid commentId,
            [FromBody] GroupCommentRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _comments.UpdateCommentAsync(CurrentUserId, commentId, request, cancellationToken));
        }

        [HttpDelete("api/group-comments/{commentId:guid}")]
        public async Task<IActionResult> DeleteComment(Guid commentId, CancellationToken cancellationToken)
        {
            await _comments.DeleteCommentAsync(CurrentUserId, commentId, cancellationToken);
            return NoContent();
        }

        // ================================================================ Lịch làm việc (dùng chung ScheduleSlot)

        // Member tự xếp giờ làm cho việc (hoặc việc con) được giao cho mình. Chồng giờ với slot khác của chính mình thì 409 SLOT_OVERLAP.
        // Sửa và hủy slot dùng PUT và DELETE /api/schedule-slots/{slotId}; lịch của mình xem ở GET /api/calendar
        [HttpPost("api/group-tasks/{taskId:guid}/schedule-slots")]
        public async Task<ActionResult<GroupSlotDto>> CreateSlot(Guid taskId, [FromBody] GroupSlotRequestDto request, CancellationToken cancellationToken)
        {
            var slot = await _schedule.CreateSlotAsync(CurrentUserId, taskId, request, cancellationToken);
            return CreatedAtAction(nameof(GetSlots), new { taskId }, slot);
        }

        // Slot của việc (kể cả các việc con), kèm người xếp. Mọi thành viên xem được
        [HttpGet("api/group-tasks/{taskId:guid}/schedule-slots")]
        public async Task<ActionResult<IReadOnlyList<GroupSlotDto>>> GetSlots(Guid taskId, [FromQuery] bool includeCancelled, CancellationToken cancellationToken)
        {
            return Ok(await _schedule.GetSlotsAsync(CurrentUserId, taskId, includeCancelled, cancellationToken));
        }

        // ================================================================ Việc của tôi

        // Việc và việc con được giao cho tôi, qua mọi nhóm đang hoạt động
        [HttpGet("api/my-group-tasks")]
        public async Task<ActionResult<MyGroupWorkDto>> GetMyGroupTasks(CancellationToken cancellationToken)
        {
            return Ok(await _progress.GetMyWorkAsync(CurrentUserId, cancellationToken));
        }
    }
}
