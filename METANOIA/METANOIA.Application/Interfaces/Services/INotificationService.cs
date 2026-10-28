using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface INotificationService
    {
        // Ghi một dòng vào bảng Notification rồi đẩy qua SignalR và Web Push. Lỗi ở hai kênh đẩy chỉ được ghi log,
        // không làm hỏng việc gọi (job nền không bị chạy lại và gửi trùng).
        System.Threading.Tasks.Task SendAsync(Guid userId, string title, string message, string type, Guid? scheduleSlotId = null);

        // Hộp thư của người dùng, mới nhất trước
        Task<IReadOnlyList<NotificationDto>> GetAsync(Guid userId, bool unreadOnly, int take);

        System.Threading.Tasks.Task MarkReadAsync(Guid userId, Guid notificationId);

        // Trả về số thông báo vừa được đánh dấu đã đọc
        Task<int> MarkAllReadAsync(Guid userId);
    }
}
