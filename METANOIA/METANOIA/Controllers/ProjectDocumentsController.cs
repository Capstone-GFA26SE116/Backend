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

        private bool IsAdmin => User.IsInRole("Admin");

        // Tài liệu của TÔI qua mọi dự án. GET /api/documents?projectId=&search=&tag=&sourceType=
        // search lọc theo tên (chứa chuỗi, không phân biệt hoa thường); sourceType là File hoặc Link
        [HttpGet("api/documents")]
        public async Task<ActionResult<IReadOnlyList<ProjectDocumentListItemDto>>> GetMyDocuments(
            [FromQuery] Guid? projectId,
            [FromQuery] string? search,
            [FromQuery] string? tag,
            [FromQuery] string? sourceType,
            CancellationToken cancellationToken)
        {
            return Ok(await _documentService.GetUserDocumentsAsync(CurrentUserId, projectId, search, tag, sourceType, cancellationToken));
        }

        // Tài liệu của một người dùng theo id. Chính mình thì được; người khác chỉ Admin được xem (người thường nhận 403)
        [HttpGet("api/documents/by-user/{userId:guid}")]
        public async Task<ActionResult<IReadOnlyList<ProjectDocumentListItemDto>>> GetDocumentsByUser(
            Guid userId,
            [FromQuery] Guid? projectId,
            [FromQuery] string? search,
            [FromQuery] string? tag,
            [FromQuery] string? sourceType,
            CancellationToken cancellationToken)
        {
            if (userId != CurrentUserId && !IsAdmin)
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            return Ok(await _documentService.GetUserDocumentsAsync(userId, projectId, search, tag, sourceType, cancellationToken));
        }

        // Admin: tài liệu của MỌI người dùng, có phân trang (page từ 1, pageSize 1 đến 100). userId để trống là tất cả
        [Authorize(Roles = "Admin")]
        [HttpGet("api/documents/all")]
        public async Task<ActionResult<PagedResultDto<ProjectDocumentListItemDto>>> GetAllDocuments(
            [FromQuery] Guid? userId,
            [FromQuery] Guid? projectId,
            [FromQuery] string? search,
            [FromQuery] string? tag,
            [FromQuery] string? sourceType,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            return Ok(await _documentService.GetAllDocumentsAsync(userId, projectId, search, tag, sourceType, page, pageSize, cancellationToken));
        }

        // Đường dẫn thứ hai cho cùng việc: GET /api/documents/by-project/{projectId}
        [HttpGet("api/projects/{projectId:guid}/documents")]
        [HttpGet("api/documents/by-project/{projectId:guid}")]
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
