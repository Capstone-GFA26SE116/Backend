using METANOIA.Application.Dto;
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

    // Thư viện nhạc tập trung. Không có DELETE vì bài đã được dùng trong FocusSession: hãy tắt bằng IsActive = false
    [Authorize(Roles = "Admin")]
    [Route("api/admin/focus-tracks")]
    [ApiController]
    public class AdminFocusTracksController : ControllerBase
    {
        private readonly IFocusTrackService _service;

        public AdminFocusTracksController(IFocusTrackService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<FocusTrackDto>>> GetTracks([FromQuery] bool? isActive, CancellationToken cancellationToken)
        {
            return Ok(await _service.GetTracksAsync(isActive, cancellationToken));
        }

        [HttpGet("{trackId:guid}")]
        public async Task<ActionResult<FocusTrackDto>> GetTrack(Guid trackId, CancellationToken cancellationToken)
        {
            return Ok(await _service.GetTrackAsync(trackId, cancellationToken));
        }

        [HttpPost]
        public async Task<ActionResult<FocusTrackDto>> CreateTrack([FromBody] FocusTrackRequestDto request, CancellationToken cancellationToken)
        {
            var created = await _service.CreateTrackAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetTrack), new { trackId = created.Id }, created);
        }

        [HttpPut("{trackId:guid}")]
        public async Task<ActionResult<FocusTrackDto>> UpdateTrack(
            Guid trackId,
            [FromBody] FocusTrackRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _service.UpdateTrackAsync(trackId, request, cancellationToken));
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
}
