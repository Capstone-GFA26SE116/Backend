using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Authorize]
    [ApiController]
    public class SubTasksController : ControllerBase
    {
        private readonly ISubTaskService _subTaskService;

        public SubTasksController(ISubTaskService subTaskService)
        {
            _subTaskService = subTaskService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet("api/tasks/{taskId:guid}/subtasks")]
        public async Task<ActionResult<IReadOnlyList<SubTaskDto>>> GetSubTasks(Guid taskId, CancellationToken cancellationToken)
        {
            return Ok(await _subTaskService.GetSubTasksAsync(CurrentUserId, taskId, cancellationToken));
        }

        [HttpPost("api/tasks/{taskId:guid}/subtasks")]
        public async Task<ActionResult<SubTaskDto>> CreateSubTask(
            Guid taskId,
            [FromBody] SubTaskRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _subTaskService.CreateSubTaskAsync(CurrentUserId, taskId, request, cancellationToken);
            return CreatedAtAction(nameof(GetSubTask), new { subTaskId = created.Id }, created);
        }

        [HttpGet("api/subtasks/{subTaskId:guid}")]
        public async Task<ActionResult<SubTaskDto>> GetSubTask(Guid subTaskId, CancellationToken cancellationToken)
        {
            return Ok(await _subTaskService.GetSubTaskAsync(CurrentUserId, subTaskId, cancellationToken));
        }

        [HttpPut("api/subtasks/{subTaskId:guid}")]
        public async Task<ActionResult<SubTaskDto>> UpdateSubTask(
            Guid subTaskId,
            [FromBody] SubTaskRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _subTaskService.UpdateSubTaskAsync(CurrentUserId, subTaskId, request, cancellationToken));
        }

        [HttpDelete("api/subtasks/{subTaskId:guid}")]
        public async Task<IActionResult> DeleteSubTask(Guid subTaskId, CancellationToken cancellationToken)
        {
            await _subTaskService.DeleteSubTaskAsync(CurrentUserId, subTaskId, cancellationToken);
            return NoContent();
        }
    }
}
