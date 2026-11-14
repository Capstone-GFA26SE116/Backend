using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Auditing;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Authorize]
    [Route("api/invoices")]
    [ApiController]
    public class InvoicesController : ControllerBase
    {
        private const string AdminRole = "Admin";

        private readonly IInvoiceService _invoiceService;

        public InvoicesController(IInvoiceService invoiceService)
        {
            _invoiceService = invoiceService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private bool IsAdmin => User.IsInRole(AdminRole);

        // Không truyền tham số thì trả mọi hóa đơn như cũ. Lọc: userId (chỉ Admin), from, to (theo ngày phát hành), search.
        // Truyền page hoặc pageSize thì phân trang; tổng số dòng khớp nằm ở header X-Total-Count
        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<InvoiceDto>>> GetInvoices(
            [FromQuery] InvoiceFilter filter, CancellationToken cancellationToken)
        {
            var (items, total) = await _invoiceService.SearchInvoicesAsync(CurrentUserId, IsAdmin, filter, cancellationToken);
            Response.Headers["X-Total-Count"] = total.ToString();
            return Ok(items);
        }

        [HttpGet("{invoiceId:guid}")]
        public async Task<ActionResult<InvoiceDto>> GetInvoice(Guid invoiceId, CancellationToken cancellationToken)
        {
            return Ok(await _invoiceService.GetInvoiceAsync(CurrentUserId, IsAdmin, invoiceId, cancellationToken));
        }

        [Authorize(Roles = AdminRole)]
        [RequireAdminReason]
        [HttpPost]
        public async Task<ActionResult<InvoiceDto>> CreateInvoice([FromBody] InvoiceRequestDto request, CancellationToken cancellationToken)
        {
            var created = await _invoiceService.CreateInvoiceAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetInvoice), new { invoiceId = created.Id }, created);
        }

        // Hóa đơn đã phát hành: chỉ sửa thông tin người mua
        [Authorize(Roles = AdminRole)]
        [RequireAdminReason]
        [HttpPut("{invoiceId:guid}")]
        public async Task<ActionResult<InvoiceDto>> UpdateInvoice(
            Guid invoiceId,
            [FromBody] InvoiceUpdateDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _invoiceService.UpdateInvoiceAsync(invoiceId, request, cancellationToken));
        }

        [Authorize(Roles = AdminRole)]
        [RequireAdminReason]
        [HttpDelete("{invoiceId:guid}")]
        public async Task<IActionResult> DeleteInvoice(Guid invoiceId, CancellationToken cancellationToken)
        {
            await _invoiceService.DeleteInvoiceAsync(invoiceId, cancellationToken);
            return NoContent();
        }
    }
}
