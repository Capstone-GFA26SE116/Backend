using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    // Truy vấn / thêm dữ liệu khởi tạo của user: project mặc định, gói, circadian, domain profile
    public interface IUserSetupRepository
    {
        Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<SubscriptionPlan?> GetPlanAsync(Guid planId, CancellationToken cancellationToken = default);

        Task<bool> HasDefaultProjectAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<bool> HasActiveSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<UserSubscription?> GetActiveSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<UserCircadianProfile>> GetCircadianProfilesAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<UserDomainProfile>> GetDomainProfilesAsync(Guid userId, CancellationToken cancellationToken = default);

        // Trong số `domainIds`, những Domain đang IsActive
        Task<IReadOnlyList<Guid>> GetActiveDomainIdsAsync(IReadOnlyCollection<Guid> domainIds, CancellationToken cancellationToken = default);

        Task<string?> GetGoogleConnectionStatusAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Guid>> GetUserIdsNeedingInitializationAsync(CancellationToken cancellationToken = default);

        Task AddProjectAsync(Project project, CancellationToken cancellationToken = default);

        Task AddSubscriptionAsync(UserSubscription subscription, CancellationToken cancellationToken = default);

        Task AddCircadianProfilesAsync(IEnumerable<UserCircadianProfile> profiles, CancellationToken cancellationToken = default);

        Task AddDomainProfilesAsync(IEnumerable<UserDomainProfile> profiles, CancellationToken cancellationToken = default);
    }
}
