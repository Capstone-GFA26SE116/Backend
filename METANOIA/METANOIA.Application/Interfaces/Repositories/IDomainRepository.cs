using DomainEntity = METANOIA.Domain.Entities.Domain;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IDomainRepository
    {
        Task<IReadOnlyList<DomainEntity>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<DomainEntity?> GetByIdAsync(Guid domainId, CancellationToken cancellationToken = default);

        Task<bool> NameExistsAsync(string name, Guid excludeId, CancellationToken cancellationToken = default);

        Task<bool> IsInUseAsync(Guid domainId, CancellationToken cancellationToken = default);

        Task AddAsync(DomainEntity domain, CancellationToken cancellationToken = default);

        void Remove(DomainEntity domain);
    }
}
