using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IClientRepository
    {
        Task<IReadOnlyList<Client>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<Client?> GetByIdForUserAsync(Guid clientId, Guid userId, CancellationToken cancellationToken = default);

        Task AddAsync(Client client, CancellationToken cancellationToken = default);

        void Remove(Client client);
    }
}
