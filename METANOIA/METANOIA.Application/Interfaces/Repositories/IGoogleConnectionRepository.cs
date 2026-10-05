using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IGoogleConnectionRepository
    {
        Task<GoogleConnection?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

        Task AddAsync(GoogleConnection connection, CancellationToken cancellationToken = default);
    }
}
