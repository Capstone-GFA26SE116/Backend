using METANOIA.Application.Interfaces.Repositories;
using Notification = METANOIA.Domain.Entities.Notification;
using METANOIA.Infrastructure.Data;

namespace METANOIA.Infrastructure.Repositories;

public sealed class NotificationRepository(AppDbContext dbContext) : INotificationRepository
{
    public Task AddAsync(Notification notification)
    {
        return dbContext.Notifications.AddAsync(notification).AsTask();
    }
}
