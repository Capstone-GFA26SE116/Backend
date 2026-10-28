using METANOIA.Domain.Entities;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface INotificationRepository
    {
        System.Threading.Tasks.Task AddAsync(Notification notification);

        Task<IReadOnlyList<Notification>> GetByUserAsync(Guid userId, bool unreadOnly, int take);

        Task<Notification?> GetByIdForUserAsync(Guid notificationId, Guid userId);

        Task<IReadOnlyList<Notification>> GetUnreadByUserAsync(Guid userId);
    }
}
