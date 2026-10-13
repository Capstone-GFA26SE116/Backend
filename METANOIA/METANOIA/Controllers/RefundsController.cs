using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Authorize]
    [Route("api/refunds")]
    [ApiController]
    public class RefundsController : ControllerBase
    {
        private const string AdminRole = "Admin";

        private readonly IRefundService _refundService;

        public RefundsController(IRefundService refundService)
        {
            _refundService = refundService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private bool IsAdmin => User.IsInRole(AdminRole);

        // status (tùy chọn): Requested | Approved | Rejected | Completed
        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<RefundDto>>> GetRefunds([FromQuery] string? status, CancellationToken cancellationToken)
        {
            return Ok(await _refundService.GetRefundsAsync(CurrentUserId, IsAdmin, status, cancellationToken));
        }

        [HttpGet("{refundId:guid}")]
        public async Task<ActionResult<RefundDto>> GetRefund(Guid refundId, CancellationToken cancellationToken)
        {
            return Ok(await _refundService.GetRefundAsync(CurrentUserId, IsAdmin, refundId, cancellationToken));
        }

        // Duyệt: thu hồi gói đăng ký gắn với giao dịch. Sau đó admin tự chuyển khoản hoàn tiền cho khách.
        [Authorize(Roles = AdminRole)]
        [HttpPost("{refundId:guid}/approve")]
        public async Task<ActionResult<RefundDto>> Approve(Guid refundId, [FromBody] RefundProcessDto request, CancellationToken cancellationToken)
        {
            return Ok(await _refundService.ApproveAsync(refundId, request, cancellationToken));
        }

        [Authorize(Roles = AdminRole)]
        [HttpPost("{refundId:guid}/reject")]
        public async Task<ActionResult<RefundDto>> Reject(Guid refundId, [FromBody] RefundProcessDto request, CancellationToken cancellationToken)
        {
            return Ok(await _refundService.RejectAsync(refundId, request, cancellationToken));
        }

        // Đóng yêu cầu sau khi đã chuyển khoản: bắt buộc nhập bankReference
        [Authorize(Roles = AdminRole)]
        [HttpPost("{refundId:guid}/complete")]
        public async Task<ActionResult<RefundDto>> Complete(Guid refundId, [FromBody] RefundProcessDto request, CancellationToken cancellationToken)
        {
            return Ok(await _refundService.CompleteAsync(refundId, request, cancellationToken));
        }
    }
}
