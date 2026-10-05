using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IGoogleCalendarConnectionService
    {
        string BuildAuthorizationUrl(Guid userId);

        Task ConnectAsync(string code, string state, CancellationToken cancellationToken = default);

        Task<GoogleCalendarStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}
