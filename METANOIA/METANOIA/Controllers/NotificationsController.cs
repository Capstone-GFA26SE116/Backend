using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace METANOIA.Controllers;

// Hộp thư thông báo của người dùng. Thông báo mới còn được đẩy realtime qua SignalR (/hubs/notifications) và Web Push;
// các API ở đây để xem lại những thông báo đã bỏ lỡ (đóng tab, mất mạng) và đánh dấu đã đọc.
// Đăng ký nhận Web Push (subscribe, unsubscribe) nằm ở PushSubscriptionController, cùng tiền tố đường dẫn.
[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController(
    INotificationService notificationService,
    IOptions<VapidSettings> vapidSettings) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotifications([FromQuery] bool unreadOnly = false, [FromQuery] int take = 50)
    {
        return Ok(await notificationService.GetAsync(GetUserId(), unreadOnly, take));
    }

    [HttpPost("{notificationId:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(Guid notificationId)
    {
        await notificationService.MarkReadAsync(GetUserId(), notificationId);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        return Ok(new { updated = await notificationService.MarkAllReadAsync(GetUserId()) });
    }

    // Khóa công khai VAPID để trình duyệt đăng ký Web Push (pushManager.subscribe). Khóa riêng không bao giờ được trả ra
    [HttpGet("vapid-public-key")]
    public IActionResult GetVapidPublicKey()
    {
        var publicKey = vapidSettings.Value.PublicKey;
        return string.IsNullOrWhiteSpace(publicKey)
            ? NotFound(new { message = "Server chưa cấu hình VapidSettings." })
            : Ok(new { publicKey });
    }

    private Guid GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var userId))
            throw new UnauthorizedAccessException("User identifier is missing or invalid in token.");
        return userId;
    }
}
