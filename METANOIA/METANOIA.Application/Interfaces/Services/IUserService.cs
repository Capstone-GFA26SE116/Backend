using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IUserService
    {
        Task<AuthResponseDto> LoginWithGoogleAsync(string idToken, CancellationToken cancellationToken = default);
    }
}
