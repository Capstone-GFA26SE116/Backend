using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class UserSubscriptionService : IUserSubscriptionService
    {
        private const string ActiveStatus = "Active";

        private static readonly HashSet<string> AllowedStatuses = new()
        {
            ActiveStatus, "Scheduled", "Expired", "Cancelled"
        };

        private readonly IUserSubscriptionRepository _subscriptionRepository;
        private readonly IUnitOfWork _unitOfWork;

        public UserSubscriptionService(IUserSubscriptionRepository subscriptionRepository, IUnitOfWork unitOfWork)
        {
            _subscriptionRepository = subscriptionRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<UserSubscriptionDto>> GetSubscriptionsAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken = default)
        {
            var subscriptions = await _subscriptionRepository.GetAsync(isAdmin ? null : userId, cancellationToken);
            return subscriptions.Select(ToDto).ToList();
        }

        public async Task<UserSubscriptionDto> GetSubscriptionAsync(Guid userId, bool isAdmin, Guid subscriptionId, CancellationToken cancellationToken = default)
        {
            var subscription = await GetExistingAsync(subscriptionId, cancellationToken);
            if (!isAdmin && subscription.UserId != userId)
            {
                throw new NotFoundException("Không tìm thấy gói đăng ký.");
            }

            return ToDto(subscription);
        }

        public async Task<UserSubscriptionDto> CreateSubscriptionAsync(UserSubscriptionRequestDto request, CancellationToken cancellationToken = default)
        {
            await ValidateAsync(request, Guid.Empty, cancellationToken);

            var subscription = new UserSubscription
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow
            };
            ApplyRequest(subscription, request);

            await _subscriptionRepository.AddAsync(subscription, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(subscription);
        }

        public async Task<UserSubscriptionDto> UpdateSubscriptionAsync(Guid subscriptionId, UserSubscriptionRequestDto request, CancellationToken cancellationToken = default)
        {
            var subscription = await GetExistingAsync(subscriptionId, cancellationToken);
            await ValidateAsync(request, subscriptionId, cancellationToken);

            ApplyRequest(subscription, request);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(subscription);
        }

        public async Task DeleteSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
        {
            var subscription = await GetExistingAsync(subscriptionId, cancellationToken);

            if (await _subscriptionRepository.HasAiUsageAsync(subscriptionId, cancellationToken))
            {
                throw new UserFriendlyException("Gói đã phát sinh lượt gọi AI nên không thể xóa. Hãy đổi trạng thái sang Cancelled thay thế.");
            }

            _subscriptionRepository.Remove(subscription);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task<UserSubscription> GetExistingAsync(Guid subscriptionId, CancellationToken cancellationToken)
        {
            return await _subscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy gói đăng ký.");
        }

        private async Task ValidateAsync(UserSubscriptionRequestDto request, Guid excludeId, CancellationToken cancellationToken)
        {
            var status = string.IsNullOrWhiteSpace(request.Status) ? ActiveStatus : request.Status;
            if (!AllowedStatuses.Contains(status))
            {
                throw new UserFriendlyException("Trạng thái không hợp lệ. Chọn một trong: Active, Scheduled, Expired, Cancelled.");
            }

            if (request.EndAt is { } endAt && endAt <= request.StartAt)
            {
                throw new UserFriendlyException("EndAt phải lớn hơn StartAt.");
            }

            if (request.AiCallsUsed < 0)
            {
                throw new UserFriendlyException("Số lượt gọi AI đã dùng không được âm.");
            }

            if (!await _subscriptionRepository.UserExistsAsync(request.UserId, cancellationToken))
            {
                throw new UserFriendlyException("Người dùng không tồn tại.");
            }

            if (!await _subscriptionRepository.PlanExistsAsync(request.PlanId, cancellationToken))
            {
                throw new UserFriendlyException("Gói đăng ký không tồn tại.");
            }

            if (request.PaymentTransactionId is { } paymentId)
            {
                if (!await _subscriptionRepository.PaymentExistsAsync(paymentId, cancellationToken))
                {
                    throw new UserFriendlyException("Giao dịch thanh toán không tồn tại.");
                }

                if (await _subscriptionRepository.PaymentLinkedToOtherSubscriptionAsync(paymentId, excludeId, cancellationToken))
                {
                    throw new UserFriendlyException("Giao dịch này đã gắn với một gói đăng ký khác.");
                }
            }

            // Tương ứng index unique partial: mỗi user chỉ có một gói Active
            if (status == ActiveStatus
                && await _subscriptionRepository.OtherActiveSubscriptionExistsAsync(request.UserId, excludeId, cancellationToken))
            {
                throw new UserFriendlyException("Người dùng này đã có một gói Active. Hãy đổi gói cũ sang Expired hoặc Cancelled trước.");
            }
        }

        private static void ApplyRequest(UserSubscription subscription, UserSubscriptionRequestDto request)
        {
            subscription.UserId = request.UserId;
            subscription.PlanId = request.PlanId;
            subscription.PaymentTransactionId = request.PaymentTransactionId;
            subscription.StartAt = request.StartAt.UtcDateTime;
            subscription.EndAt = request.EndAt?.UtcDateTime;
            subscription.Status = string.IsNullOrWhiteSpace(request.Status) ? ActiveStatus : request.Status;
            subscription.AiCallsUsed = request.AiCallsUsed;
            subscription.QuotaResetAt = (request.QuotaResetAt ?? request.StartAt).UtcDateTime;
        }

        private static UserSubscriptionDto ToDto(UserSubscription subscription)
        {
            return new UserSubscriptionDto
            {
                Id = subscription.Id,
                UserId = subscription.UserId,
                PlanId = subscription.PlanId,
                PaymentTransactionId = subscription.PaymentTransactionId,
                StartAt = UtcTime.ToApi(subscription.StartAt),
                EndAt = UtcTime.ToApi(subscription.EndAt),
                Status = subscription.Status,
                AiCallsUsed = subscription.AiCallsUsed,
                QuotaResetAt = UtcTime.ToApi(subscription.QuotaResetAt),
                CreatedAt = UtcTime.ToApi(subscription.CreatedAt)
            };
        }
    }
}
