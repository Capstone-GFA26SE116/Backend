using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface ISubscriptionPlanRepository
    {
        Task<IReadOnlyList<SubscriptionPlan>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<SubscriptionPlan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken = default);

        Task<bool> NameExistsAsync(string name, Guid excludeId, CancellationToken cancellationToken = default);

        Task<bool> IsInUseAsync(Guid planId, CancellationToken cancellationToken = default);

        Task AddAsync(SubscriptionPlan plan, CancellationToken cancellationToken = default);

        void Remove(SubscriptionPlan plan);
    }
}
