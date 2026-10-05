using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IGoogleTokenValidator
    {
        Task<GoogleUserInfoDto> ValidateAsync(string idToken, CancellationToken cancellationToken = default);
    }
}
