using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IAdminUserService
    {
        Task<PagedResultDto<AdminUserListItemDto>> SearchAsync(
            string? search, string? role, bool includeDeleted, int page, int pageSize, CancellationToken cancellationToken = default);

        Task<AdminUserDetailDto> GetAsync(Guid userId, CancellationToken cancellationToken = default);
    }

    public interface IAiUsageReportService
    {
        // groupBy: feature | model | day. Mặc định 30 ngày gần nhất
        Task<AiUsageReportDto> GetReportAsync(string? groupBy, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default);
    }

    public interface IFocusTrackService
    {
        Task<IReadOnlyList<FocusTrackDto>> GetTracksAsync(bool? isActive, CancellationToken cancellationToken = default);

        Task<FocusTrackDto> GetTrackAsync(Guid trackId, CancellationToken cancellationToken = default);

        Task<FocusTrackDto> CreateTrackAsync(FocusTrackRequestDto request, CancellationToken cancellationToken = default);

        Task<FocusTrackDto> UpdateTrackAsync(Guid trackId, FocusTrackRequestDto request, CancellationToken cancellationToken = default);
    }

    public interface IAdminAuditLogService
    {
        Task<PagedResultDto<AdminAuditLogDto>> SearchAsync(
            Guid? adminId, string? targetType, Guid? targetId, string? action,
            DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken cancellationToken = default);
    }
}
