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

        public Task<GoogleConnection?> GetWithEventsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.GoogleConnections
                .Include(c => c.GoogleCalendarEvents)
                .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
        }

        public Task<GoogleConnection?> GetByWatchChannelIdAsync(string channelId, CancellationToken cancellationToken = default)
        {
            return _context.GoogleConnections
                .FirstOrDefaultAsync(c => c.WatchChannelId == channelId, cancellationToken);
        }

        public async Task AddAsync(GoogleConnection connection, CancellationToken cancellationToken = default)
        {
            await _context.GoogleConnections.AddAsync(connection, cancellationToken);
        }

        public void Remove(GoogleConnection connection)
        {
            // Event đã cache đi theo connection (khóa ngoại ON DELETE CASCADE, và EF xóa luôn các event đã nạp)
            _context.GoogleConnections.Remove(connection);
        }
    }
}
