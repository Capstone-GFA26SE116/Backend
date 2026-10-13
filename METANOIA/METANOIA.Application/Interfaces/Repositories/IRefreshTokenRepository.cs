using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IRefreshTokenRepository
    {
        Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default);

        Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

        // Token của user chưa bị thu hồi và chưa hết hạn
        Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, DateTime now, CancellationToken cancellationToken = default);
    }
}
