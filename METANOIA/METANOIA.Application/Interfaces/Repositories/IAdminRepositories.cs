using METANOIA.Application.Dto;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IAdminUserRepository
    {
        Task<PagedResultDto<AdminUserListItemDto>> SearchAsync(
            string? search, string? role, bool includeDeleted, int page, int pageSize, CancellationToken cancellationToken = default);

        Task<AdminUserDetailDto?> GetDetailAsync(Guid userId, CancellationToken cancellationToken = default);
    }

    public interface IAiUsageRepository
    {
        // groupBy: feature | model | day. Chỉ lấy log trong [from, to)
        Task<IReadOnlyList<AiUsageRowDto>> SummarizeAsync(string groupBy, DateTime from, DateTime to, CancellationToken cancellationToken = default);

        Task<AiUsageRowDto> TotalAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    }

    public interface IFocusTrackRepository
    {
        Task<IReadOnlyList<FocusTrack>> GetAsync(bool? isActive, CancellationToken cancellationToken = default);

        Task<FocusTrack?> GetByIdAsync(Guid trackId, CancellationToken cancellationToken = default);

        Task AddAsync(FocusTrack track, CancellationToken cancellationToken = default);
    }

    public interface IAdminAuditLogRepository
    {
        Task<(IReadOnlyList<(AdminActionLog Log, string AdminEmail)> Items, int TotalCount)> SearchAsync(
            Guid? adminId, string? targetType, Guid? targetId, string? action,
            DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken = default);
    }
}
