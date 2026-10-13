using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IGoogleConnectionRepository
    {
        Task<GoogleConnection?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

        // Kèm các event đã cache, để xóa connection thì event đi theo
        Task<GoogleConnection?> GetWithEventsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<GoogleConnection?> GetByWatchChannelIdAsync(string channelId, CancellationToken cancellationToken = default);

        Task AddAsync(GoogleConnection connection, CancellationToken cancellationToken = default);

        void Remove(GoogleConnection connection);
    }
}
