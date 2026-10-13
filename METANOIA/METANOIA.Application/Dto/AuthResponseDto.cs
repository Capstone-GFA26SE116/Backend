namespace METANOIA.Application.Dto
{
    public class AuthResponseDto
    {
        public Guid UserId { get; set; }

        public string Email { get; set; } = null!;

        public string FullName { get; set; } = null!;

        public string Role { get; set; } = null!;

        public string AccessToken { get; set; } = null!;

        public DateTime ExpiresAtUtc { get; set; }

        // Dùng với POST /api/auth/refresh khi AccessToken hết hạn. Chỉ trả ra đúng một lần, server chỉ giữ bản băm
        public string RefreshToken { get; set; } = null!;

        public DateTime RefreshTokenExpiresAtUtc { get; set; }
    }
}
