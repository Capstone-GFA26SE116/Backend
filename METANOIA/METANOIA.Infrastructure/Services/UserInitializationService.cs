using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Dữ liệu khởi tạo mà thiết kế yêu cầu mỗi user phải có: project "Cá nhân", gói Miễn phí, 24 dòng circadian.
    public class UserInitializationService : IUserInitializationService
    {
        public const string PersonalProjectName = "Cá nhân";
        public const string OnboardingSource = "Onboarding";
        public const decimal DefaultCircadianScore = 0.500m;
        public const int HoursPerDay = 24;

        private const string ActiveStatus = "Active";

        private readonly IUserSetupRepository _setup;
        private readonly IUnitOfWork _unitOfWork;
        private readonly SubscriptionOptions _options;
        private readonly ILogger<UserInitializationService> _logger;

        public UserInitializationService(
            IUserSetupRepository setup,
            IUnitOfWork unitOfWork,
            IOptions<SubscriptionOptions> options,
            ILogger<UserInitializationService> logger)
        {
            _setup = setup;
            _unitOfWork = unitOfWork;
            _options = options.Value;
            _logger = logger;
        }

        public async Task InitializeNewUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            await StageProjectAsync(userId, cancellationToken);
            await StageFreeSubscriptionAsync(userId, cancellationToken);
            await StageCircadianProfilesAsync(userId, cancellationToken);

            // Một lần SaveChanges duy nhất = một transaction: có lỗi thì không lưu gì (kể cả User đang chờ)
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<bool> StageFreeSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (await _setup.HasActiveSubscriptionAsync(userId, cancellationToken))
            {
                return false;
            }

            var plan = await GetFreePlanAsync(cancellationToken);
            var now = DateTime.UtcNow;

            await _setup.AddSubscriptionAsync(new UserSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlanId = plan.Id,
                PaymentTransactionId = null,
                StartAt = now,
                EndAt = null,
                Status = ActiveStatus,
                AiCallsUsed = 0,
                QuotaResetAt = now,
                CreatedAt = now
            }, cancellationToken);

            return true;
        }

        public Task<IReadOnlyList<Guid>> GetUserIdsNeedingInitializationAsync(CancellationToken cancellationToken = default)
        {
            return _setup.GetUserIdsNeedingInitializationAsync(cancellationToken);
        }

        private async Task StageProjectAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (await _setup.HasDefaultProjectAsync(userId, cancellationToken))
            {
                return;
            }

            await _setup.AddProjectAsync(new Project
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ClientId = null,
                Name = PersonalProjectName,
                Status = "Active",
                IsDefault = true,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);
        }

        private async Task StageCircadianProfilesAsync(Guid userId, CancellationToken cancellationToken)
        {
            var existingHours = (await _setup.GetCircadianProfilesAsync(userId, cancellationToken))
                .Select(p => (int)p.HourOfDay)
                .ToHashSet();

            var now = DateTime.UtcNow;
            var missing = Enumerable.Range(0, HoursPerDay)
                .Where(hour => !existingHours.Contains(hour))
                .Select(hour => new UserCircadianProfile
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    HourOfDay = (short)hour,
                    Score = DefaultCircadianScore,
                    Source = OnboardingSource,
                    UpdatedAt = now
                })
                .ToList();

            if (missing.Count > 0)
            {
                await _setup.AddCircadianProfilesAsync(missing, cancellationToken);
            }
        }

        private async Task<SubscriptionPlan> GetFreePlanAsync(CancellationToken cancellationToken)
        {
            if (_options.FreePlanId == Guid.Empty)
            {
                _logger.LogError("Chưa cấu hình Subscription:FreePlanId trong appsettings.");
                throw new InvalidOperationException("Chưa cấu hình 'Subscription:FreePlanId' (Id của gói Miễn phí).");
            }

            var plan = await _setup.GetPlanAsync(_options.FreePlanId, cancellationToken);
            if (plan is null || !plan.IsActive || plan.Price != 0)
            {
                _logger.LogError("Subscription:FreePlanId = {PlanId} không trỏ tới một gói Miễn phí hợp lệ (tồn tại, IsActive, Price = 0).", _options.FreePlanId);
                throw new InvalidOperationException(
                    $"Subscription:FreePlanId ({_options.FreePlanId}) không trỏ tới một gói Miễn phí hợp lệ (phải tồn tại, IsActive và Price = 0).");
            }

            return plan;
        }
    }
}
