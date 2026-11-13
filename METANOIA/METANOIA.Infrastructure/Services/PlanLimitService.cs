using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;

namespace METANOIA.Infrastructure.Services
{
    public class PlanLimitService : IPlanLimitService
    {
        private const long BytesPerMb = 1024L * 1024;

        private readonly IUserSubscriptionRepository _subscriptions;
        private readonly IProjectRepository _projects;
        private readonly IProjectDocumentRepository _documents;

        public PlanLimitService(
            IUserSubscriptionRepository subscriptions,
            IProjectRepository projects,
            IProjectDocumentRepository documents)
        {
            _subscriptions = subscriptions;
            _projects = projects;
            _documents = documents;
        }

        public async Task EnsureCanCreateProjectAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var limits = await _subscriptions.GetActivePlanLimitsAsync(userId, cancellationToken);
            if (limits?.ProjectLimit is not { } max)
            {
                return;
            }

            var used = await _projects.CountCountableAsync(userId, cancellationToken);
            if (used >= max)
            {
                throw new UserFriendlyException(
                    $"Gói {limits.PlanName} chỉ cho tối đa {max} dự án đang hoạt động (bạn đang có {used}). " +
                    "Hãy lưu trữ bớt dự án hoặc nâng cấp gói.");
            }
        }

        public async Task EnsureCanAddFileAsync(Guid userId, long newBytes, CancellationToken cancellationToken = default)
        {
            var limits = await _subscriptions.GetActivePlanLimitsAsync(userId, cancellationToken);
            if (limits is null)
            {
                return;
            }

            var used = await _documents.SumFileBytesByUserAsync(userId, cancellationToken);
            var maxBytes = limits.StorageLimitMb * BytesPerMb;
            if (used + newBytes > maxBytes)
            {
                throw new UserFriendlyException(
                    $"Gói {limits.PlanName} cho tối đa {limits.StorageLimitMb} MB dung lượng file. " +
                    $"Bạn đã dùng {ToMb(used)} MB, tải thêm {ToMb(newBytes)} MB sẽ vượt hạn mức. " +
                    "Hãy xóa bớt file hoặc nâng cấp gói.");
            }
        }

        public async Task<PlanUsageDto> GetUsageAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var limits = await _subscriptions.GetActivePlanLimitsAsync(userId, cancellationToken);

            return new PlanUsageDto
            {
                PlanName = limits?.PlanName,
                ProjectLimit = limits?.ProjectLimit,
                StorageLimitMb = limits?.StorageLimitMb,
                SubscriptionStartAt = limits is null ? null : UtcTime.ToApi(limits.StartAt),
                SubscriptionEndAt = UtcTime.ToApi(limits?.EndAt),
                RemainingDays = UtcTime.RemainingDays(limits?.EndAt, DateTime.UtcNow),
                ProjectsUsed = await _projects.CountCountableAsync(userId, cancellationToken),
                StorageUsedBytes = await _documents.SumFileBytesByUserAsync(userId, cancellationToken)
            };
        }

        private static string ToMb(long bytes) => (bytes / (double)BytesPerMb).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
