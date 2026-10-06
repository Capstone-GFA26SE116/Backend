using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Services;

namespace METANOIA.Test.Infrastructure
{
    // Token giả có dạng "valid:<subject>:<email>:<họ tên>"; mọi token khác bị từ chối như Google thật.
    public class FakeGoogleTokenValidator : IGoogleTokenValidator
    {
        public Task<GoogleUserInfoDto> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
        {
            var parts = idToken.Split(':');
            if (parts.Length != 4 || parts[0] != "valid")
            {
                throw new UserFriendlyException("Google token không hợp lệ hoặc đã hết hạn.");
            }

            return Task.FromResult(new GoogleUserInfoDto
            {
                Subject = parts[1],
                Email = parts[2],
                FullName = parts[3],
                EmailVerified = true
            });
        }
    }
}
