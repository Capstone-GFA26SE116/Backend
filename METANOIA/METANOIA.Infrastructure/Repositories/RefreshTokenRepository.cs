using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly AppDbContext _context;

        public RefreshTokenRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default)
        {
            await _context.RefreshTokens.AddAsync(token, cancellationToken);
        }

        public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            return _context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
        }

        public async Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, DateTime now, CancellationToken cancellationToken = default)
        {
            return await _context.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
                .ToListAsync(cancellationToken);
        }
    }
}
