using METANOIA.Application.Interfaces.Repositories;
using Notification = METANOIA.Domain.Entities.Notification;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace METANOIA.Infrastructure.Repositories;

public sealed class NotificationRepository(AppDbContext dbContext) : INotificationRepository
{
    public System.Threading.Tasks.Task AddAsync(Notification notification)
    {
        return dbContext.Notifications.AddAsync(notification).AsTask();
    }

    public async Task<IReadOnlyList<Notification>> GetByUserAsync(Guid userId, bool unreadOnly, int take)
    {
        var query = dbContext.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }

        return await query
            .OrderByDescending(n => n.SentAt ?? n.ScheduledAt)
            .Take(take)
            .ToListAsync();
    }

    public Task<Notification?> GetByIdForUserAsync(Guid notificationId, Guid userId)
    {
        return dbContext.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);
    }

    public async Task<IReadOnlyList<Notification>> GetUnreadByUserAsync(Guid userId)
    {
        return await dbContext.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync();
    }
}
