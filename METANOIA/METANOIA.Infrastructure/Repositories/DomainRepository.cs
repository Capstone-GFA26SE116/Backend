using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using DomainEntity = METANOIA.Domain.Entities.Domain;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class DomainRepository : IDomainRepository
    {
        private readonly AppDbContext _context;

        public DomainRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<DomainEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _context.Domains
                .OrderBy(d => d.Name)
                .ToListAsync(cancellationToken);
        }

        public Task<DomainEntity?> GetByIdAsync(Guid domainId, CancellationToken cancellationToken = default)
        {
            return _context.Domains.FirstOrDefaultAsync(d => d.Id == domainId, cancellationToken);
        }

        public Task<bool> NameExistsAsync(string name, Guid excludeId, CancellationToken cancellationToken = default)
        {
            var normalized = name.ToLower();
            return _context.Domains.AnyAsync(d => d.Id != excludeId && d.Name.ToLower() == normalized, cancellationToken);
        }

        public async Task<bool> IsInUseAsync(Guid domainId, CancellationToken cancellationToken = default)
        {
            return await _context.Tasks.AnyAsync(t => t.DomainId == domainId, cancellationToken)
                || await _context.UserDomainProfiles.AnyAsync(p => p.DomainId == domainId, cancellationToken);
        }

        public async Task AddAsync(DomainEntity domain, CancellationToken cancellationToken = default)
        {
            await _context.Domains.AddAsync(domain, cancellationToken);
        }

        public void Remove(DomainEntity domain)
        {
            _context.Domains.Remove(domain);
        }
    }
}
