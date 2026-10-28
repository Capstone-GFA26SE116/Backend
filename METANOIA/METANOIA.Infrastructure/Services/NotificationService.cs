using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Configuration;
using Notification = METANOIA.Domain.Entities.Notification;
using METANOIA.Infrastructure.Notifications;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebPush;

namespace METANOIA.Infrastructure.Services;

public sealed class NotificationService(
    IHubContext<NotificationHub, INotificationClient> hubContext,
    INotificationRepository notificationRepository,
    IUserPushSubscriptionRepository pushSubscriptionRepository,
    IUnitOfWork unitOfWork,
    IOptions<VapidSettings> vapidSettings,
    ILogger<NotificationService> logger) : INotificationService
{
    public async System.Threading.Tasks.Task SendAsync(Guid userId, string title, string message, string type, Guid? scheduleSlotId = null)
    {
        var now = DateTime.UtcNow;
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ScheduleSlotId = scheduleSlotId,
            Type = ToStoredType(type),
            Title = title,
            Message = message,
            ScheduledAt = now,
            SentAt = now,
            IsRead = false
        };

        // Lưu trước: dù hai kênh đẩy bên dưới lỗi thì thông báo vẫn nằm trong hộp thư (GET /api/notifications)
        await notificationRepository.AddAsync(notification);
        await unitOfWork.SaveChangesAsync();

        try
        {
            await hubContext.Clients.User(userId.ToString())
                .ReceiveNotification(message, type, notification.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không đẩy được thông báo {Type} qua SignalR cho user {UserId}.", type, userId);
        }

        try
        {
            await SendWebPushAsync(userId, title, message, type, vapidSettings.Value);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không gửi được Web Push {Type} cho user {UserId}.", type, userId);
        }
    }

    // Bảng Notification có CHECK (Notification_Type_check) chỉ cho 'SlotReminder', 'MissedSlot', 'DeadlineRisk'.
    // "type" truyền vào là loại chi tiết (START_PROMPT, NEAR_END, OVERRUN...) vẫn được gửi nguyên cho SignalR và Web Push
    // để giao diện biết phải làm gì; cột Type trong DB chỉ lưu nhóm lớn. Ghi loại chi tiết thẳng vào DB sẽ bị Postgres từ chối.
    public const string MissedSlotAction = "MISSED_SLOT";

    private static string ToStoredType(string actionType)
    {
        return actionType == MissedSlotAction ? "MissedSlot" : "SlotReminder";
    }

    public async Task<IReadOnlyList<NotificationDto>> GetAsync(Guid userId, bool unreadOnly, int take)
    {
        var items = await notificationRepository.GetByUserAsync(userId, unreadOnly, Math.Clamp(take, 1, 100));
        return items.Select(ToDto).ToList();
    }

    public async System.Threading.Tasks.Task MarkReadAsync(Guid userId, Guid notificationId)
    {
        var notification = await notificationRepository.GetByIdForUserAsync(notificationId, userId)
            ?? throw new NotFoundException("Không tìm thấy thông báo.");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await unitOfWork.SaveChangesAsync();
        }
    }

    public async Task<int> MarkAllReadAsync(Guid userId)
    {
        var unread = await notificationRepository.GetUnreadByUserAsync(userId);
        var now = DateTime.UtcNow;
        foreach (var notification in unread)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        if (unread.Count > 0)
        {
            await unitOfWork.SaveChangesAsync();
        }

        return unread.Count;
    }

    private async System.Threading.Tasks.Task SendWebPushAsync(
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
            action = actionType
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
                // Trình duyệt đã hủy đăng ký: dọn dòng đó đi
                pushSubscriptionRepository.Remove(subscription);
                removeExpired = true;
            }
            catch (Exception ex)
            {
                // Một thiết bị lỗi không được chặn các thiết bị còn lại
                logger.LogWarning(ex, "Web Push tới một thiết bị của user {UserId} thất bại.", userId);
            }
        }

        if (removeExpired)
            await unitOfWork.SaveChangesAsync();
    }

    private static NotificationDto ToDto(Notification notification)
    {
        return new NotificationDto
        {
            Id = notification.Id,
            Type = notification.Type,
            Title = notification.Title,
            Message = notification.Message,
            ScheduleSlotId = notification.ScheduleSlotId,
            SentAt = ToApi(notification.SentAt ?? notification.ScheduledAt),
            IsRead = notification.IsRead,
            ReadAt = notification.ReadAt is { } readAt ? ToApi(readAt) : null
        };
    }

    private static DateTimeOffset ToApi(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);
    }
}
