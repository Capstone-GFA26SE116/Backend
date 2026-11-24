using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using METANOIA.Infrastructure.Repositories;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test.Infrastructure
{
    // Repository thật nhưng có thể được bật để ném lỗi ở bước cấp gói, mô phỏng sự cố giữa chừng khi khởi tạo user
    public class FailingUserSetupRepository : UserSetupRepository
    {
        private readonly Func<bool> _failOnSubscriptionCreate;

        public FailingUserSetupRepository(AppDbContext context, Func<bool> failOnSubscriptionCreate) : base(context)
        {
            _failOnSubscriptionCreate = failOnSubscriptionCreate;
        }

        public override Task AddSubscriptionAsync(UserSubscription subscription, CancellationToken cancellationToken = default)
        {
            if (_failOnSubscriptionCreate())
            {
                throw new InvalidOperationException("Lỗi giả lập khi cấp gói.");
            }

            return base.AddSubscriptionAsync(subscription, cancellationToken);
        }
    }
}
