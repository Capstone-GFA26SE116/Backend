namespace METANOIA.Application.Dto
{
    public class GoogleUserInfoDto
    {
        public string Subject { get; set; } = null!;

        public string Email { get; set; } = null!;

        public string FullName { get; set; } = null!;

        public bool EmailVerified { get; set; }
    }
}
