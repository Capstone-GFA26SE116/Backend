using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace METANOIA.Infrastructure.Repositories
{
    public class UnitOfWork : IUnitOfWork
    {
        private const string InMemoryProvider = "Microsoft.EntityFrameworkCore.InMemory";

        private readonly AppDbContext _context;

        public UnitOfWork(AppDbContext context)
        {
            _context = context;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _context.SaveChangesAsync(cancellationToken);
        }

        public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            // Provider InMemory (dùng cho test) không hỗ trợ transaction
            if (_context.Database.ProviderName == InMemoryProvider || _context.Database.CurrentTransaction is not null)
            {
                await action();
                return;
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            await action();
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
