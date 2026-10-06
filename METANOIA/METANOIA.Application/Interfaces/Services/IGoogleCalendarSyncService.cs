using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IGoogleCalendarSyncService
    {
        Task<GoogleCalendarSyncResultDto> SyncAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}
