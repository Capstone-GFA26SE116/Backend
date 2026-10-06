using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class ClientRepository : IClientRepository
    {
        private readonly AppDbContext _context;

        public ClientRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<Client>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Clients
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.Name)
                .ToListAsync(cancellationToken);
        }

        public Task<Client?> GetByIdForUserAsync(Guid clientId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Clients
                .FirstOrDefaultAsync(c => c.Id == clientId && c.UserId == userId, cancellationToken);
        }

        public async Task AddAsync(Client client, CancellationToken cancellationToken = default)
        {
            await _context.Clients.AddAsync(client, cancellationToken);
        }

        public void Remove(Client client)
        {
            _context.Clients.Remove(client);
        }
    }
}
