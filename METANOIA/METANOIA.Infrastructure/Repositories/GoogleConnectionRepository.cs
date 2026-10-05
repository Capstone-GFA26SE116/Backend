using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class GoogleConnectionRepository : IGoogleConnectionRepository
    {
        private readonly AppDbContext _context;

        public GoogleConnectionRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<GoogleConnection?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.GoogleConnections
                .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
        }

        public async Task AddAsync(GoogleConnection connection, CancellationToken cancellationToken = default)
        {
            await _context.GoogleConnections.AddAsync(connection, cancellationToken);
        }
    }
}
