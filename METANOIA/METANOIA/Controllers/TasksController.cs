using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Authorize]
    [ApiController]
    public class TasksController : ControllerBase
    {
        private readonly ITaskService _taskService;

        public TasksController(ITaskService taskService)
        {
            _taskService = taskService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet("api/projects/{projectId:guid}/tasks")]
        public async Task<ActionResult<IReadOnlyList<TaskDto>>> GetTasks(
            Guid projectId,
            [FromQuery] string? status,
            CancellationToken cancellationToken)
        {
            return Ok(await _taskService.GetTasksAsync(CurrentUserId, projectId, status, cancellationToken));
        }

        [HttpPost("api/projects/{projectId:guid}/tasks")]
        public async Task<ActionResult<TaskDto>> CreateTask(
            Guid projectId,
            [FromBody] TaskRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _taskService.CreateTaskAsync(CurrentUserId, projectId, request, cancellationToken);
            return CreatedAtAction(nameof(GetTask), new { taskId = created.Id }, created);
        }

        [HttpGet("api/tasks/{taskId:guid}")]
        public async Task<ActionResult<TaskDto>> GetTask(Guid taskId, CancellationToken cancellationToken)
        {
            return Ok(await _taskService.GetTaskAsync(CurrentUserId, taskId, cancellationToken));
        }

        [HttpPut("api/tasks/{taskId:guid}")]
        public async Task<ActionResult<TaskDto>> UpdateTask(
            Guid taskId,
            [FromBody] TaskRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _taskService.UpdateTaskAsync(CurrentUserId, taskId, request, cancellationToken));
        }

        [HttpDelete("api/tasks/{taskId:guid}")]
        public async Task<IActionResult> DeleteTask(Guid taskId, CancellationToken cancellationToken)
        {
            await _taskService.DeleteTaskAsync(CurrentUserId, taskId, cancellationToken);
            return NoContent();
        }
    }
}
