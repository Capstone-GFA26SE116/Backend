using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class UserSetupRepository : IUserSetupRepository
    {
        private const string ActiveStatus = "Active";
        private const int HoursPerDay = 24;

        private readonly AppDbContext _context;

        public UserSetupRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        }

        public Task<SubscriptionPlan?> GetPlanAsync(Guid planId, CancellationToken cancellationToken = default)
        {
            return _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        }

        public Task<bool> HasDefaultProjectAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Projects.AnyAsync(p => p.UserId == userId && p.IsDefault, cancellationToken);
        }

        public Task<bool> HasActiveSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.UserSubscriptions.AnyAsync(s => s.UserId == userId && s.Status == ActiveStatus, cancellationToken);
        }

        public Task<UserSubscription?> GetActiveSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.UserSubscriptions.Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ActiveStatus, cancellationToken);
        }

        public async Task<IReadOnlyList<UserCircadianProfile>> GetCircadianProfilesAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.UserCircadianProfiles.Where(p => p.UserId == userId).ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<UserDomainProfile>> GetDomainProfilesAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.UserDomainProfiles.Where(p => p.UserId == userId).ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Guid>> GetActiveDomainIdsAsync(IReadOnlyCollection<Guid> domainIds, CancellationToken cancellationToken = default)
        {
            return await _context.Domains
                .Where(d => domainIds.Contains(d.Id) && d.IsActive)
                .Select(d => d.Id)
                .ToListAsync(cancellationToken);
        }

        public Task<string?> GetGoogleConnectionStatusAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.GoogleConnections
                .Where(c => c.UserId == userId)
                .Select(c => (string?)c.Status)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Guid>> GetUserIdsNeedingInitializationAsync(CancellationToken cancellationToken = default)
        {
            return await _context.Users
                .Where(u => u.DeletedAt == null
                    && (!_context.Projects.Any(p => p.UserId == u.Id && p.IsDefault)
                        || !_context.UserSubscriptions.Any(s => s.UserId == u.Id && s.Status == ActiveStatus)
                        || _context.UserCircadianProfiles.Count(c => c.UserId == u.Id) < HoursPerDay))
                .OrderBy(u => u.CreatedAt)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);
        }

        public virtual async Task AddProjectAsync(Project project, CancellationToken cancellationToken = default)
        {
            await _context.Projects.AddAsync(project, cancellationToken);
        }

        public async Task AddSubscriptionAsync(UserSubscription subscription, CancellationToken cancellationToken = default)
        {
            await _context.UserSubscriptions.AddAsync(subscription, cancellationToken);
        }

        public async Task AddCircadianProfilesAsync(IEnumerable<UserCircadianProfile> profiles, CancellationToken cancellationToken = default)
        {
            await _context.UserCircadianProfiles.AddRangeAsync(profiles, cancellationToken);
        }

        public async Task AddDomainProfilesAsync(IEnumerable<UserDomainProfile> profiles, CancellationToken cancellationToken = default)
        {
            await _context.UserDomainProfiles.AddRangeAsync(profiles, cancellationToken);
        }
    }
}
