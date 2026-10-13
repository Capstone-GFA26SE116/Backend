using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IGoogleCalendarConnectionService
    {
        string BuildAuthorizationUrl(Guid userId);

        Task ConnectAsync(string code, string state, CancellationToken cancellationToken = default);

        Task<GoogleCalendarStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default);

        // Hủy liên kết: xóa GoogleConnection (và các event đã cache), rồi thu hồi quyền ở Google. Gọi khi chưa kết nối vẫn an toàn.
        Task DisconnectAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}
