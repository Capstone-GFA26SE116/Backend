using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class ClientService : IClientService
    {
        private const int MaxNameLength = 100;
        private const int MaxEmailLength = 255;
        private const int MaxPhoneLength = 20;

        private readonly IClientRepository _clientRepository;
        private readonly IUnitOfWork _unitOfWork;

        public ClientService(IClientRepository clientRepository, IUnitOfWork unitOfWork)
        {
            _clientRepository = clientRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<ClientDto>> GetClientsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var clients = await _clientRepository.GetByUserIdAsync(userId, cancellationToken);
            return clients.Select(ToDto).ToList();
        }

        public async Task<ClientDto> GetClientAsync(Guid userId, Guid clientId, CancellationToken cancellationToken = default)
        {
            var client = await GetOwnedClientAsync(userId, clientId, cancellationToken);
            return ToDto(client);
        }

        public async Task<ClientDto> CreateClientAsync(Guid userId, ClientRequestDto request, CancellationToken cancellationToken = default)
        {
            ValidateRequest(request);

            var client = new Client { Id = Guid.NewGuid(), UserId = userId };
            ApplyRequest(client, request);

            await _clientRepository.AddAsync(client, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(client);
        }

        public async Task<ClientDto> UpdateClientAsync(Guid userId, Guid clientId, ClientRequestDto request, CancellationToken cancellationToken = default)
        {
            ValidateRequest(request);

            var client = await GetOwnedClientAsync(userId, clientId, cancellationToken);
            ApplyRequest(client, request);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(client);
        }

        public async Task DeleteClientAsync(Guid userId, Guid clientId, CancellationToken cancellationToken = default)
        {
            var client = await GetOwnedClientAsync(userId, clientId, cancellationToken);
            _clientRepository.Remove(client);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task<Client> GetOwnedClientAsync(Guid userId, Guid clientId, CancellationToken cancellationToken)
        {
            return await _clientRepository.GetByIdForUserAsync(clientId, userId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy khách hàng.");
        }

        private static void ValidateRequest(ClientRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength)
            {
                throw new UserFriendlyException($"Tên khách hàng không được để trống và tối đa {MaxNameLength} ký tự.");
            }

            if (!string.IsNullOrWhiteSpace(request.ContactEmail)
                && (!request.ContactEmail.Contains('@') || request.ContactEmail.Length > MaxEmailLength))
            {
                throw new UserFriendlyException("Email liên hệ không hợp lệ.");
            }

            if (request.Phone is not null && request.Phone.Length > MaxPhoneLength)
            {
                throw new UserFriendlyException($"Số điện thoại tối đa {MaxPhoneLength} ký tự.");
            }
        }

        private static void ApplyRequest(Client client, ClientRequestDto request)
        {
            client.Name = request.Name.Trim();
            client.ContactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim();
            client.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
            client.Notes = request.Notes;
        }

        private static ClientDto ToDto(Client client)
        {
            return new ClientDto
            {
                Id = client.Id,
                Name = client.Name,
                ContactEmail = client.ContactEmail,
                Phone = client.Phone,
                Notes = client.Notes,
                CreatedAt = client.CreatedAt
            };
        }
    }
}
