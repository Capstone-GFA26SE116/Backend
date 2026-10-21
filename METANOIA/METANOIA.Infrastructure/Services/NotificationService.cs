using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Configuration;
using Notification = METANOIA.Domain.Entities.Notification;
using METANOIA.Infrastructure.Notifications;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using WebPush;

namespace METANOIA.Infrastructure.Services;

public sealed class NotificationService(
    IHubContext<NotificationHub, INotificationClient> hubContext,
    INotificationRepository notificationRepository,
    IUserPushSubscriptionRepository pushSubscriptionRepository,
    IUnitOfWork unitOfWork,
    IOptions<VapidSettings> vapidSettings) : INotificationService
{
    public async Task SendAsync(Guid userId, string title, string message, string type, Guid? scheduleSlotId = null)
    {
        var now = DateTime.UtcNow;
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ScheduleSlotId = scheduleSlotId,
            Type = type,
            Title = title,
            Message = message,
            ScheduledAt = now,
            SentAt = now,
            IsRead = false
        };

        await notificationRepository.AddAsync(notification);
        await unitOfWork.SaveChangesAsync();

        await hubContext.Clients.User(userId.ToString())
            .ReceiveNotification(message, type, notification.Id);

        await SendWebPushAsync(userId, title, message, type, vapidSettings.Value);
    }

    private async Task SendWebPushAsync(
        Guid userId,
        string title,
        string message,
        string actionType,
        VapidSettings settings)
    {
        var subscriptions = await pushSubscriptionRepository.GetByUserIdAsync(userId);
        if (subscriptions.Count == 0 || string.IsNullOrWhiteSpace(settings.Subject)
            || string.IsNullOrWhiteSpace(settings.PublicKey)
            || string.IsNullOrWhiteSpace(settings.PrivateKey))
            return;

        var client = new WebPushClient();
        var vapidDetails = new VapidDetails(settings.Subject, settings.PublicKey, settings.PrivateKey);
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            title,
            body = message,
            action = actionType,
            icon = "/assets/icon.png"
        });

        var removeExpired = false;
        foreach (var subscription in subscriptions)
        {
            try
            {
                await client.SendNotificationAsync(
                    new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth),
                    payload,
                    vapidDetails);
            }
            catch (WebPushException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Gone or System.Net.HttpStatusCode.NotFound)
            {
                pushSubscriptionRepository.Remove(subscription);
                removeExpired = true;
            }
        }

        if (removeExpired)
            await unitOfWork.SaveChangesAsync();
    }
}
