using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IClientService
    {
        Task<IReadOnlyList<ClientDto>> GetClientsAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<ClientDto> GetClientAsync(Guid userId, Guid clientId, CancellationToken cancellationToken = default);

        Task<ClientDto> CreateClientAsync(Guid userId, ClientRequestDto request, CancellationToken cancellationToken = default);

        Task<ClientDto> UpdateClientAsync(Guid userId, Guid clientId, ClientRequestDto request, CancellationToken cancellationToken = default);

        Task DeleteClientAsync(Guid userId, Guid clientId, CancellationToken cancellationToken = default);
    }
}
