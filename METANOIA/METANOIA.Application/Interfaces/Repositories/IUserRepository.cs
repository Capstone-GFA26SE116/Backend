using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IUserRepository
    {
        Task<User?> GetByGoogleSubjectIdAsync(string googleSubjectId, CancellationToken cancellationToken = default);

        Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

        Task<Role?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default);

        // false nếu user không tồn tại hoặc đã bị xóa mềm
        Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken = default);

        Task AddAsync(User user, CancellationToken cancellationToken = default);
    }
}
