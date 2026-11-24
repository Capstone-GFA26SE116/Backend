using METANOIA.Application.Dto;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    // Truy vấn nhóm cho Admin: không lọc theo thành viên
    public interface IAdminGroupRepository
    {
        // search: tên nhóm hoặc email/họ tên người sở hữu. status: Active | Archived (rỗng = cả hai). Mới tạo trước
        Task<(IReadOnlyList<AdminGroupListItemDto> Items, int TotalCount)> SearchAsync(
            string? search, string? status, Guid? ownerId, int page, int pageSize, CancellationToken cancellationToken = default);

        // Nhóm kèm người sở hữu (không theo dõi); null nếu không có
        Task<WorkGroup?> GetGroupWithOwnerAsync(Guid groupId, CancellationToken cancellationToken = default);

        Task<AdminGroupStatsDto> GetStatsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
    }
}
