using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using METANOIA.Infrastructure.Repositories;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test.Infrastructure
{
    // Repository thật nhưng có thể được bật để ném lỗi ở bước tạo project, mô phỏng sự cố giữa chừng khi khởi tạo user
    public class FailingUserSetupRepository : UserSetupRepository
    {
        private readonly Func<bool> _failOnProjectCreate;

        public FailingUserSetupRepository(AppDbContext context, Func<bool> failOnProjectCreate) : base(context)
        {
            _failOnProjectCreate = failOnProjectCreate;
        }

        public override Task AddProjectAsync(Project project, CancellationToken cancellationToken = default)
        {
            if (_failOnProjectCreate())
            {
                throw new InvalidOperationException("Lỗi giả lập khi tạo project.");
            }

            return base.AddProjectAsync(project, cancellationToken);
        }
    }
}
