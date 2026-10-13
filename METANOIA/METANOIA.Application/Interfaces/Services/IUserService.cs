using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IUserService
    {
        Task<AuthResponseDto> LoginWithGoogleAsync(string idToken, CancellationToken cancellationToken = default);

        // Đổi refresh token lấy access token mới + refresh token mới (token cũ bị thu hồi).
        // Token cũ bị đem ra dùng lại sau thời gian ân hạn thì thu hồi toàn bộ token của user.
        Task<AuthResponseDto> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

        // Thu hồi refresh token (hoặc tất cả token của user nếu allDevices). Luôn thành công, kể cả token lạ hoặc đã thu hồi
        Task LogoutAsync(string refreshToken, bool allDevices, CancellationToken cancellationToken = default);
    }
}
