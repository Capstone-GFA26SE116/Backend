using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IGoogleCalendarSyncService
    {
        // Kéo từ Google về, theo syncToken (webhook và hành vi cũ của POST /api/google-calendar/sync)
        Task<GoogleCalendarSyncResultDto> SyncAsync(Guid userId, CancellationToken cancellationToken = default);

        // Đồng bộ theo hướng và khoảng ngày người dùng chọn
        Task<GoogleCalendarSyncResultDto> SyncAsync(Guid userId, GoogleCalendarSyncRequest request, CancellationToken cancellationToken = default);
    }
}
