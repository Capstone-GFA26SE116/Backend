namespace METANOIA.Application.Dto
{
    public class RefreshRequestDto
    {
        public string RefreshToken { get; set; } = null!;
    }

    public class LogoutRequestDto
    {
        public string RefreshToken { get; set; } = null!;

        // true: đăng xuất khỏi mọi thiết bị (thu hồi tất cả refresh token của user)
        public bool AllDevices { get; set; }
    }
}
