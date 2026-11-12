using DomainTask = METANOIA.Domain.Entities.Task;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface ITaskRepository
    {
        Task<bool> ProjectOwnedByUserAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<DomainTask>> GetByProjectIdAsync(Guid projectId, string? status, CancellationToken cancellationToken = default);

        Task<DomainTask?> GetByIdForUserAsync(Guid taskId, Guid userId, CancellationToken cancellationToken = default);

        Task<bool> MilestoneBelongsToProjectAsync(Guid milestoneId, Guid projectId, CancellationToken cancellationToken = default);

        Task<bool> DomainExistsAsync(Guid domainId, CancellationToken cancellationToken = default);

        Task<bool> HasHistoryAsync(Guid taskId, CancellationToken cancellationToken = default);

        // Task có phiên làm việc đang chạy (EndedAt rỗng) không
        Task<bool> HasRunningSessionAsync(Guid taskId, CancellationToken cancellationToken = default);

        // Trạng thái của giai đoạn (null nếu không có giai đoạn)
        Task<string?> GetMilestoneStatusAsync(Guid milestoneId, CancellationToken cancellationToken = default);

        Task AddAsync(DomainTask task, CancellationToken cancellationToken = default);

        void Remove(DomainTask task);
    }
}
