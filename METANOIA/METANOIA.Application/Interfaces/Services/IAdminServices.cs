using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IAdminUserService
    {
        Task<PagedResultDto<AdminUserListItemDto>> SearchAsync(
            string? search, string? role, bool includeDeleted, int page, int pageSize, CancellationToken cancellationToken = default);

        Task<AdminUserDetailDto> GetAsync(Guid userId, CancellationToken cancellationToken = default);

        // Tạo tài khoản trước khi chủ nhân đăng nhập: có sẵn gói Miễn phí và dữ liệu circadian
        Task<AdminUserDetailDto> CreateAsync(AdminUserRequestDto request, CancellationToken cancellationToken = default);

        Task<AdminUserDetailDto> UpdateAsync(Guid userId, AdminUserRequestDto request, CancellationToken cancellationToken = default);

        // Xóa mềm. Không cho tự xóa mình và không cho xóa Admin cuối cùng
        Task DeleteAsync(Guid actingAdminId, Guid userId, CancellationToken cancellationToken = default);

        Task<AdminUserDetailDto> RestoreAsync(Guid userId, CancellationToken cancellationToken = default);
    }

    // Quản trị nhóm (nhánh Group): xem, thống kê, lưu trữ và khôi phục. Chưa có đổi Leader hoặc xóa hẳn nhóm
    public interface IAdminGroupService
    {
        Task<PagedResultDto<AdminGroupListItemDto>> SearchAsync(
            string? search, string? status, Guid? ownerId, int page, int pageSize, CancellationToken cancellationToken = default);

        Task<AdminGroupDetailDto> GetAsync(Guid groupId, CancellationToken cancellationToken = default);

        // Nhật ký hoạt động của nhóm, mới nhất trước
        Task<IReadOnlyList<GroupActivityDto>> GetActivityAsync(Guid groupId, int skip, int take, CancellationToken cancellationToken = default);

        Task<AdminGroupStatsDto> GetStatsAsync(CancellationToken cancellationToken = default);

        // Lưu trữ hoặc khôi phục nhóm. reason là lý do của Admin: ghi vào nhật ký nhóm và gửi cho Leader
        Task<AdminGroupDetailDto> ArchiveAsync(Guid adminId, Guid groupId, string reason, CancellationToken cancellationToken = default);

        Task<AdminGroupDetailDto> RestoreAsync(Guid adminId, Guid groupId, string reason, CancellationToken cancellationToken = default);
    }

    public interface IAiUsageReportService
    {
        // groupBy: feature | model | day. Mặc định 30 ngày gần nhất
        Task<AiUsageReportDto> GetReportAsync(string? groupBy, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default);
    }

    public interface IStatusResyncService
    {
        // Suy lại trạng thái Task (và Milestone) chưa đóng theo luật liền mạch. dryRun = true: chỉ báo cáo, không lưu
        Task<StatusResyncReportDto> ResyncAsync(bool dryRun, CancellationToken cancellationToken = default);
    }

    public interface IAdminAuditLogService
    {
        Task<PagedResultDto<AdminAuditLogDto>> SearchAsync(
            Guid? adminId, string? targetType, Guid? targetId, string? action,
            DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken cancellationToken = default);
    }
}
