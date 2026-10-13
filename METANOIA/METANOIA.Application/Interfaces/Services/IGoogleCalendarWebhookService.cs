namespace METANOIA.Application.Interfaces.Services
{
    public enum GoogleWebhookResult
    {
        // Đã đồng bộ lại lịch của user
        Processed,
        // Hợp lệ nhưng không cần làm gì (tin "sync" lúc tạo kênh, kênh không còn thuộc user nào, đồng bộ lỗi)
        Ignored,
        // Thiếu hoặc sai token của kênh
        Unauthorized
    }

    public interface IGoogleCalendarWebhookService
    {
        // channelId, token, resourceState lấy từ các header X-Goog-Channel-ID, X-Goog-Channel-Token, X-Goog-Resource-State
        Task<GoogleWebhookResult> HandleAsync(string? channelId, string? token, string? resourceState, CancellationToken cancellationToken = default);
    }
}
