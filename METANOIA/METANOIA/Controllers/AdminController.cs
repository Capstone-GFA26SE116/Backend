using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Auditing;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    // Quản lý người dùng (chỉ đọc): danh sách phân trang và chi tiết
    [Authorize(Roles = "Admin")]
    [Route("api/admin/users")]
    [ApiController]
    public class AdminUsersController : ControllerBase
    {
        private readonly IAdminUserService _service;

        public AdminUsersController(IAdminUserService service)
        {
            _service = service;
        }

        // search: tìm theo email hoặc họ tên. role: tên role (ví dụ Freelancer, Admin). includeDeleted: kèm tài khoản đã xóa mềm
        [HttpGet]
        public async Task<ActionResult<PagedResultDto<AdminUserListItemDto>>> Search(
            [FromQuery] string? search,
            [FromQuery] string? role,
            [FromQuery] bool includeDeleted,
            CancellationToken cancellationToken,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            return Ok(await _service.SearchAsync(search, role, includeDeleted, page, pageSize, cancellationToken));
        }

        [HttpGet("{userId:guid}")]
        public async Task<ActionResult<AdminUserDetailDto>> Get(Guid userId, CancellationToken cancellationToken)
        {
            return Ok(await _service.GetAsync(userId, cancellationToken));
        }

        // Tạo tài khoản trước khi chủ nhân đăng nhập. Email là bắt buộc; người đó đăng nhập Google bằng đúng email này sẽ nhận tài khoản
        [HttpPost]
        public async Task<ActionResult<AdminUserDetailDto>> Create([FromBody] AdminUserRequestDto request, CancellationToken cancellationToken)
        {
            var created = await _service.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { userId = created.Id }, created);
        }

        // Trường nào để trống (null) thì giữ nguyên. Đổi role chỉ có hiệu lực với token mới (token cũ giữ role cũ đến khi hết hạn)
        [HttpPut("{userId:guid}")]
        public async Task<ActionResult<AdminUserDetailDto>> Update(
            Guid userId,
            [FromBody] AdminUserRequestDto request,
            CancellationToken cancellationToken)
        {
            // Đổi role là việc nhạy cảm: bắt buộc ghi lý do (các chỉnh sửa khác thì tùy chọn)
            if (!string.IsNullOrWhiteSpace(request.Role)) AdminReason.Require(HttpContext);

            return Ok(await _service.UpdateAsync(userId, request, cancellationToken));
        }

        // Xóa mềm: thu hồi mọi phiên đăng nhập, hủy liên kết Google Calendar. Không tự xóa mình, không xóa Admin cuối cùng
        [RequireAdminReason]
        [HttpDelete("{userId:guid}")]
        public async Task<IActionResult> Delete(Guid userId, CancellationToken cancellationToken)
        {
            var adminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _service.DeleteAsync(adminId, userId, cancellationToken);
            return NoContent();
        }

        [HttpPost("{userId:guid}/restore")]
        public async Task<ActionResult<AdminUserDetailDto>> Restore(Guid userId, CancellationToken cancellationToken)
        {
            return Ok(await _service.RestoreAsync(userId, cancellationToken));
        }
    }

    // Quản trị nhóm (nhánh Group): xem, thống kê, lưu trữ và khôi phục. Chưa có đổi Leader hay xóa hẳn nhóm.
    // Admin chỉ thấy thông tin cấp nhóm (thành viên, tiến độ, nhật ký), không thấy bình luận hay lịch cá nhân
    [Authorize(Roles = "Admin")]
    [Route("api/admin/groups")]
    [ApiController]
    public class AdminGroupsController : ControllerBase
    {
        private readonly IAdminGroupService _service;
        private readonly IAdminAuditContext _audit;

        public AdminGroupsController(IAdminGroupService service, IAdminAuditContext audit)
        {
            _service = service;
            _audit = audit;
        }

        // search: tên nhóm hoặc email/họ tên chủ nhóm. status: Active | Archived. ownerId: lọc theo người tạo
        [HttpGet]
        public async Task<ActionResult<PagedResultDto<AdminGroupListItemDto>>> Search(
            [FromQuery] string? search,
            [FromQuery] string? status,
            [FromQuery] Guid? ownerId,
            CancellationToken cancellationToken,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            return Ok(await _service.SearchAsync(search, status, ownerId, page, pageSize, cancellationToken));
        }

        // Số liệu tổng: nhóm, thành viên, việc theo trạng thái, lời mời đang chờ, phiên bấm giờ đang chạy
        [HttpGet("stats")]
        public async Task<ActionResult<AdminGroupStatsDto>> GetStats(CancellationToken cancellationToken)
        {
            return Ok(await _service.GetStatsAsync(cancellationToken));
        }

        // Chi tiết một nhóm: chủ nhóm, thành viên, báo cáo tiến độ
        [HttpGet("{groupId:guid}")]
        public async Task<ActionResult<AdminGroupDetailDto>> Get(Guid groupId, CancellationToken cancellationToken)
        {
            return Ok(await _service.GetAsync(groupId, cancellationToken));
        }

        // Nhật ký hoạt động của nhóm, mới nhất trước
        [HttpGet("{groupId:guid}/activity")]
        public async Task<ActionResult<IReadOnlyList<GroupActivityDto>>> GetActivity(
            Guid groupId, CancellationToken cancellationToken, [FromQuery] int skip = 0, [FromQuery] int take = 50)
        {
            return Ok(await _service.GetActivityAsync(groupId, skip, take, cancellationToken));
        }

        // Bắt buộc gửi header X-Admin-Reason. Lý do được ghi vào AdminActionLog, vào nhật ký nhóm và gửi thông báo cho Leader
        [RequireAdminReason]
        [HttpPost("{groupId:guid}/archive")]
        public async Task<ActionResult<AdminGroupDetailDto>> Archive(Guid groupId, CancellationToken cancellationToken)
        {
            var adminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _service.ArchiveAsync(adminId, groupId, _audit.Reason, cancellationToken));
        }

        [RequireAdminReason]
        [HttpPost("{groupId:guid}/restore")]
        public async Task<ActionResult<AdminGroupDetailDto>> Restore(Guid groupId, CancellationToken cancellationToken)
        {
            var adminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _service.RestoreAsync(adminId, groupId, _audit.Reason, cancellationToken));
        }
    }

    // Chi phí AI gộp theo feature, model hoặc ngày
    [Authorize(Roles = "Admin")]
    [Route("api/admin/ai-usage")]
    [ApiController]
    public class AdminAiUsageController : ControllerBase
    {
        private readonly IAiUsageReportService _service;

        public AdminAiUsageController(IAiUsageReportService service)
        {
            _service = service;
        }

        // groupBy: feature (mặc định) | model | day. from/to mặc định là 30 ngày gần nhất, tối đa 366 ngày
        [HttpGet]
        public async Task<ActionResult<AiUsageReportDto>> Get(
            [FromQuery] string? groupBy,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            CancellationToken cancellationToken)
        {
            return Ok(await _service.GetReportAsync(groupBy, from, to, cancellationToken));
        }
    }

    // Báo cáo doanh thu theo ngày, tháng hoặc gói. Chỉ đọc
    [Authorize(Roles = "Admin")]
    [Route("api/admin/revenue")]
    [ApiController]
    public class AdminRevenueController : ControllerBase
    {
        private readonly IRevenueReportService _service;

        public AdminRevenueController(IRevenueReportService service)
        {
            _service = service;
        }

        // groupBy: day (mặc định) | month | plan. from/to mặc định là 30 ngày gần nhất, tối đa 366 ngày. Giờ UTC; gồm from, không gồm to
        [HttpGet]
        public async Task<ActionResult<RevenueReportDto>> Get(
            [FromQuery] string? groupBy,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            CancellationToken cancellationToken)
        {
            return Ok(await _service.GetReportAsync(groupBy, from, to, cancellationToken));
        }
    }

    // Đọc AdminActionLog. Log được ghi tự động cho mọi thao tác ghi của Admin; gửi header X-Admin-Reason để ghi lý do
    [Authorize(Roles = "Admin")]
    [Route("api/admin/audit-logs")]
    [ApiController]
    public class AdminAuditLogsController : ControllerBase
    {
        private readonly IAdminAuditLogService _service;

        public AdminAuditLogsController(IAdminAuditLogService service)
        {
            _service = service;
        }

        // action: ví dụ "SubscriptionPlan.Update". targetType: ví dụ "Domain". Mới nhất trước
        [HttpGet]
        public async Task<ActionResult<PagedResultDto<AdminAuditLogDto>>> Search(
            [FromQuery] Guid? adminId,
            [FromQuery] string? targetType,
            [FromQuery] Guid? targetId,
            [FromQuery] string? action,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            CancellationToken cancellationToken,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            return Ok(await _service.SearchAsync(adminId, targetType, targetId, action, from, to, page, pageSize, cancellationToken));
        }
    }

    // Đồng bộ lại trạng thái cho dữ liệu cũ (xem docs/STATUS_WORKFLOW.md mục 9)
    [Authorize(Roles = "Admin")]
    [Route("api/admin/status-resync")]
    [ApiController]
    public class AdminStatusResyncController : ControllerBase
    {
        private readonly IStatusResyncService _service;

        public AdminStatusResyncController(IStatusResyncService service)
        {
            _service = service;
        }

        // dryRun mặc định là TRUE (chỉ báo cáo, không lưu). Muốn áp dụng thật phải gửi dryRun=false
        [HttpPost]
        public async Task<ActionResult<StatusResyncReportDto>> Run([FromQuery] bool dryRun = true, CancellationToken cancellationToken = default)
        {
            // Chạy thật sửa dữ liệu của nhiều người dùng: bắt buộc ghi lý do (chạy thử thì không)
            if (!dryRun) AdminReason.Require(HttpContext);

            return Ok(await _service.ResyncAsync(dryRun, cancellationToken));
        }
    }
}
