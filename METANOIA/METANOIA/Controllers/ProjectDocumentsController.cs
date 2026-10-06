using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    public class DocumentFileUploadForm
    {
        public IFormFile File { get; set; } = null!;

        public string? Name { get; set; }

        public string? Tag { get; set; }
    }

    [Authorize]
    [ApiController]
    public class ProjectDocumentsController : ControllerBase
    {
        private readonly IProjectDocumentService _documentService;

        public ProjectDocumentsController(IProjectDocumentService documentService)
        {
            _documentService = documentService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet("api/projects/{projectId:guid}/documents")]
        public async Task<ActionResult<IReadOnlyList<ProjectDocumentDto>>> GetDocuments(
            Guid projectId,
            [FromQuery] string? search,
            [FromQuery] string? tag,
            CancellationToken cancellationToken)
        {
            return Ok(await _documentService.GetDocumentsAsync(CurrentUserId, projectId, search, tag, cancellationToken));
        }

        [HttpPost("api/projects/{projectId:guid}/documents/files")]
        [RequestSizeLimit(21L * 1024 * 1024)]
        public async Task<ActionResult<ProjectDocumentDto>> UploadFile(
            Guid projectId,
            [FromForm] DocumentFileUploadForm form,
            CancellationToken cancellationToken)
        {
            await using var stream = form.File.OpenReadStream();
            var created = await _documentService.CreateFileDocumentAsync(
                CurrentUserId, projectId, form.Name, form.Tag,
                form.File.FileName, form.File.Length, stream, cancellationToken);

            return CreatedAtAction(nameof(GetDocument), new { documentId = created.Id }, created);
        }

        [HttpPost("api/projects/{projectId:guid}/documents/links")]
        public async Task<ActionResult<ProjectDocumentDto>> CreateLink(
            Guid projectId,
            [FromBody] ProjectDocumentRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _documentService.CreateLinkDocumentAsync(CurrentUserId, projectId, request, cancellationToken);
            return CreatedAtAction(nameof(GetDocument), new { documentId = created.Id }, created);
        }

        [HttpGet("api/documents/{documentId:guid}")]
        public async Task<ActionResult<ProjectDocumentDto>> GetDocument(Guid documentId, CancellationToken cancellationToken)
        {
            return Ok(await _documentService.GetDocumentAsync(CurrentUserId, documentId, cancellationToken));
        }

        [HttpPut("api/documents/{documentId:guid}")]
        public async Task<ActionResult<ProjectDocumentDto>> UpdateDocument(
            Guid documentId,
            [FromBody] ProjectDocumentRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _documentService.UpdateDocumentAsync(CurrentUserId, documentId, request, cancellationToken));
        }

        [HttpDelete("api/documents/{documentId:guid}")]
        public async Task<IActionResult> DeleteDocument(Guid documentId, CancellationToken cancellationToken)
        {
            await _documentService.DeleteDocumentAsync(CurrentUserId, documentId, cancellationToken);
            return NoContent();
        }

        [HttpGet("api/documents/{documentId:guid}/download")]
        public async Task<IActionResult> Download(Guid documentId, CancellationToken cancellationToken)
        {
            var download = await _documentService.OpenDownloadAsync(CurrentUserId, documentId, cancellationToken);
            return File(download.Content, "application/octet-stream", download.FileName);
        }
    }
}
