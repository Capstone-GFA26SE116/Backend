using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Authorize]
    [Route("api/projects")]
    [ApiController]
    public class ProjectsController : ControllerBase
    {
        private readonly IProjectService _projectService;

        public ProjectsController(IProjectService projectService)
        {
            _projectService = projectService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<ProjectDto>>> GetProjects(
            [FromQuery] bool includeArchived,
            CancellationToken cancellationToken)
        {
            return Ok(await _projectService.GetProjectsAsync(CurrentUserId, includeArchived, cancellationToken));
        }

        [HttpGet("{projectId:guid}")]
        public async Task<ActionResult<ProjectDto>> GetProject(Guid projectId, CancellationToken cancellationToken)
        {
            return Ok(await _projectService.GetProjectAsync(CurrentUserId, projectId, cancellationToken));
        }

        [HttpPost]
        public async Task<ActionResult<ProjectDto>> CreateProject(
            [FromBody] ProjectRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _projectService.CreateProjectAsync(CurrentUserId, request, cancellationToken);
            return CreatedAtAction(nameof(GetProject), new { projectId = created.Id }, created);
        }

        [HttpPut("{projectId:guid}")]
        public async Task<ActionResult<ProjectDto>> UpdateProject(
            Guid projectId,
            [FromBody] ProjectRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _projectService.UpdateProjectAsync(CurrentUserId, projectId, request, cancellationToken));
        }

        [HttpPost("{projectId:guid}/archive")]
        public async Task<ActionResult<ProjectDto>> ArchiveProject(Guid projectId, CancellationToken cancellationToken)
        {
            return Ok(await _projectService.ArchiveProjectAsync(CurrentUserId, projectId, cancellationToken));
        }

        [HttpPost("{projectId:guid}/restore")]
        public async Task<ActionResult<ProjectDto>> RestoreProject(Guid projectId, CancellationToken cancellationToken)
        {
            return Ok(await _projectService.RestoreProjectAsync(CurrentUserId, projectId, cancellationToken));
        }

        [HttpGet("{projectId:guid}/progress")]
        public async Task<ActionResult<ProjectProgressDto>> GetProgress(Guid projectId, CancellationToken cancellationToken)
        {
            return Ok(await _projectService.GetProgressAsync(CurrentUserId, projectId, cancellationToken));
        }
    }
}
