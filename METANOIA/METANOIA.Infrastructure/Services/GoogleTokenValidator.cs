using Google.Apis.Auth;
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;

namespace METANOIA.Infrastructure.Services
{
    public class GoogleTokenValidator : IGoogleTokenValidator
    {
        private readonly string _clientId;

        public GoogleTokenValidator(IConfiguration configuration)
        {
            _clientId = configuration["Google:ClientId"]
                ?? throw new InvalidOperationException("Cấu hình 'Google:ClientId' chưa được thiết lập.");
        }

        public async Task<GoogleUserInfoDto> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
        {
            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _clientId }
                });
            }
            catch (InvalidJwtException ex)
            {
                throw new UserFriendlyException("Google token không hợp lệ hoặc đã hết hạn.", ex);
            }

            if (!payload.EmailVerified)
            {
                throw new UserFriendlyException("Email Google chưa được xác minh.");
            }

            return new GoogleUserInfoDto
            {
                Subject = payload.Subject,
                Email = payload.Email,
                FullName = payload.Name,
                EmailVerified = payload.EmailVerified
            };
        }
    }
}
