namespace METANOIA.Application.Exceptions
{
    // Refresh token không tồn tại, hết hạn, đã bị thu hồi hoặc thuộc tài khoản đã xóa: client phải đăng nhập lại (HTTP 401)
    public class InvalidRefreshTokenException : Exception
    {
        public InvalidRefreshTokenException(string message) : base(message)
        {
        }
    }
}
