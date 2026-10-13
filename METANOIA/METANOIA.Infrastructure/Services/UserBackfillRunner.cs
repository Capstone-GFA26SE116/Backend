using METANOIA.Application.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace METANOIA.Infrastructure.Services
{
    // Bù dữ liệu khởi tạo cho user cũ (project "Cá nhân", gói Miễn phí, 24 dòng circadian).
    // Chạy một lần bằng: dotnet run --project METANOIA/METANOIA -- --backfill-user-data
    // An toàn khi chạy nhiều lần vì InitializeNewUserAsync là idempotent.
    public static class UserBackfillRunner
    {
        public const string CommandLineFlag = "--backfill-user-data";

        public record Result(int Total, int Initialized, int Failed);

        public static async Task<Result> RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(UserBackfillRunner));

            IReadOnlyList<Guid> userIds;
            using (var scope = scopeFactory.CreateScope())
            {
                userIds = await scope.ServiceProvider.GetRequiredService<IUserInitializationService>()
                    .GetUserIdsNeedingInitializationAsync(cancellationToken);
            }

            var initialized = 0;
            var failed = 0;

            foreach (var userId in userIds)
            {
                // Mỗi user một scope: lỗi của user này không làm hỏng DbContext của user khác
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IUserInitializationService>()
                        .InitializeNewUserAsync(userId, cancellationToken);
                    initialized++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    logger.LogError(ex, "Không bù được dữ liệu khởi tạo cho user {UserId}.", userId);
                }
            }

            return new Result(userIds.Count, initialized, failed);
        }
    }
}
