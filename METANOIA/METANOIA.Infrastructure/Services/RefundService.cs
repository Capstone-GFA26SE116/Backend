using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // PayOS không có API hoàn tiền cho merchant: hệ thống chỉ ghi nhận và theo dõi,
    // admin tự chuyển khoản hoàn lại rồi nhập mã giao dịch ngân hàng để đóng yêu cầu.
    public class RefundService : IRefundService
    {
        private const string Requested = "Requested";
        private const string Approved = "Approved";
        private const string Rejected = "Rejected";
        private const string Completed = "Completed";
        private const int MaxReasonLength = 500;

        private static readonly HashSet<string> AllowedStatuses = new() { Requested, Approved, Rejected, Completed };

        private readonly IPaymentRefundRepository _refunds;
        private readonly IPaymentTransactionRepository _payments;
        private readonly IUserSubscriptionRepository _subscriptions;
        private readonly IUnitOfWork _unitOfWork;
        private readonly PaymentOptions _options;

        public RefundService(
            IPaymentRefundRepository refunds,
            IPaymentTransactionRepository payments,
            IUserSubscriptionRepository subscriptions,
            IUnitOfWork unitOfWork,
            IOptions<PaymentOptions> options)
        {
            _refunds = refunds;
            _payments = payments;
            _subscriptions = subscriptions;
            _unitOfWork = unitOfWork;
            _options = options.Value;
        }

        public async Task<RefundDto> RequestRefundAsync(Guid userId, Guid paymentId, RefundRequestDto request, CancellationToken cancellationToken = default)
        {
            var reason = request.Reason?.Trim();
            if (string.IsNullOrEmpty(reason))
            {
                throw new UserFriendlyException("Vui lòng nhập lý do hoàn tiền.");
            }

            if (reason.Length > MaxReasonLength)
            {
                throw new UserFriendlyException($"Lý do hoàn tiền tối đa {MaxReasonLength} ký tự.");
            }

            var payment = await _payments.GetByIdAsync(paymentId, cancellationToken);
            if (payment is null || payment.UserId != userId)
            {
                throw new NotFoundException("Không tìm thấy giao dịch thanh toán.");
            }

            if (payment.Status != "Succeeded")
            {
                throw new UserFriendlyException("Chỉ có thể hoàn tiền cho giao dịch đã thanh toán thành công.");
            }

            if (await _refunds.GetActiveByPaymentAsync(paymentId, cancellationToken) is { } existing)
            {
                throw new UserFriendlyException($"Giao dịch này đã có yêu cầu hoàn tiền (trạng thái {existing.Status}).");
            }

            var paidAt = payment.PaidAt ?? payment.CreatedAt;
            if (DateTime.UtcNow > paidAt.AddDays(_options.RefundWindowDays))
            {
                throw new UserFriendlyException($"Đã quá {_options.RefundWindowDays} ngày kể từ lúc thanh toán nên không thể tự yêu cầu hoàn tiền. Vui lòng liên hệ hỗ trợ.");
            }

            var subscription = await _subscriptions.GetByPaymentIdAsync(paymentId, cancellationToken);
            if (subscription is { AiCallsUsed: > 0 })
            {
                throw new UserFriendlyException("Gói đã được sử dụng nên không thể tự yêu cầu hoàn tiền. Vui lòng liên hệ hỗ trợ.");
            }

            var refund = new PaymentRefund
            {
                Id = Guid.NewGuid(),
                PaymentTransactionId = paymentId,
                PaymentTransaction = payment,
                Amount = payment.Amount,
                Reason = reason,
                Source = "User",
                Status = Requested,
                CreatedAt = DateTime.UtcNow
            };

            await _refunds.AddAsync(refund, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(refund);
        }

        public async Task<IReadOnlyList<RefundDto>> GetRefundsAsync(Guid userId, bool isAdmin, string? status, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(status) && !AllowedStatuses.Contains(status))
            {
                throw new UserFriendlyException("Trạng thái không hợp lệ. Chọn một trong: Requested, Approved, Rejected, Completed.");
            }

            var refunds = await _refunds.GetAsync(isAdmin ? null : userId, status, cancellationToken);
            return refunds.Select(ToDto).ToList();
        }

        public async Task<RefundDto> GetRefundAsync(Guid userId, bool isAdmin, Guid refundId, CancellationToken cancellationToken = default)
        {
            var refund = await GetExistingAsync(refundId, cancellationToken);
            if (!isAdmin && refund.PaymentTransaction.UserId != userId)
            {
                throw new NotFoundException("Không tìm thấy yêu cầu hoàn tiền.");
            }

            return ToDto(refund);
        }

        public async Task<RefundDto> ApproveAsync(Guid refundId, RefundProcessDto request, CancellationToken cancellationToken = default)
        {
            var refund = await GetExistingAsync(refundId, cancellationToken);
            EnsureStatus(refund, Requested, "duyệt");

            // Duyệt hoàn tiền là thu hồi quyền sử dụng ngay, không chờ đến khi chuyển khoản xong
            var subscription = await _subscriptions.GetByPaymentIdAsync(refund.PaymentTransactionId, cancellationToken);
            if (subscription is { Status: "Active" or "Scheduled" })
            {
                if (subscription.Status == "Active")
                {
                    subscription.EndAt = DateTime.UtcNow;
                }

                subscription.Status = "Cancelled";
            }

            refund.Status = Approved;
            refund.AdminNote = Clean(request.Note);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(refund);
        }

        public async Task<RefundDto> RejectAsync(Guid refundId, RefundProcessDto request, CancellationToken cancellationToken = default)
        {
            var refund = await GetExistingAsync(refundId, cancellationToken);
            EnsureStatus(refund, Requested, "từ chối");

            var note = Clean(request.Note);
            if (note is null)
            {
                throw new UserFriendlyException("Vui lòng nhập lý do từ chối.");
            }

            refund.Status = Rejected;
            refund.AdminNote = note;
            refund.ProcessedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(refund);
        }

        public async Task<RefundDto> CompleteAsync(Guid refundId, RefundProcessDto request, CancellationToken cancellationToken = default)
        {
            var refund = await GetExistingAsync(refundId, cancellationToken);
            EnsureStatus(refund, Approved, "hoàn tất");

            var bankReference = Clean(request.BankReference);
            if (bankReference is null)
            {
                throw new UserFriendlyException("Vui lòng nhập mã giao dịch ngân hàng của lần chuyển khoản hoàn tiền.");
            }

            refund.Status = Completed;
            refund.BankReference = bankReference.Length > 100 ? bankReference[..100] : bankReference;
            refund.AdminNote = Clean(request.Note) ?? refund.AdminNote;
            refund.ProcessedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(refund);
        }

        private async Task<PaymentRefund> GetExistingAsync(Guid refundId, CancellationToken cancellationToken)
        {
            return await _refunds.GetByIdAsync(refundId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy yêu cầu hoàn tiền.");
        }

        private static void EnsureStatus(PaymentRefund refund, string expected, string action)
        {
            if (refund.Status != expected)
            {
                throw new UserFriendlyException($"Không thể {action} yêu cầu đang ở trạng thái {refund.Status} (cần {expected}).");
            }
        }

        private static string? Clean(string? value)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return null;
            }

            return trimmed.Length > MaxReasonLength ? trimmed[..MaxReasonLength] : trimmed;
        }

        private static RefundDto ToDto(PaymentRefund refund)
        {
            return new RefundDto
            {
                Id = refund.Id,
                PaymentTransactionId = refund.PaymentTransactionId,
                UserId = refund.PaymentTransaction.UserId,
                Amount = refund.Amount,
                Reason = refund.Reason,
                Source = refund.Source,
                Status = refund.Status,
                AdminNote = refund.AdminNote,
                BankReference = refund.BankReference,
                CreatedAt = UtcTime.ToApi(refund.CreatedAt),
                ProcessedAt = UtcTime.ToApi(refund.ProcessedAt)
            };
        }
    }
}
