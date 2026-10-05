using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class AuthRepository : IUserRepository
    {
        private readonly AppDbContext _context;

        public AuthRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<User?> GetByGoogleSubjectIdAsync(string googleSubjectId, CancellationToken cancellationToken = default)
        {
            return _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.GoogleSubjectId == googleSubjectId, cancellationToken);
        }

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            return _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        }

        public Task<Role?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default)
        {
            return _context.Roles.FirstOrDefaultAsync(r => r.Name == roleName, cancellationToken);
        }

        public async Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            await _context.Users.AddAsync(user, cancellationToken);
        }
    }
}
