using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class PaymentTransactionService : IPaymentTransactionService
    {
        private const string PendingStatus = "Pending";

        private static readonly HashSet<string> AllowedGateways = new() { "PayOS", "VNPay" };

        private static readonly HashSet<string> AllowedStatuses = new()
        {
            PendingStatus, "Succeeded", "Failed", "Cancelled", "Expired"
        };

        private readonly IPaymentTransactionRepository _paymentRepository;
        private readonly IUnitOfWork _unitOfWork;

        public PaymentTransactionService(IPaymentTransactionRepository paymentRepository, IUnitOfWork unitOfWork)
        {
            _paymentRepository = paymentRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<PaymentTransactionDto>> GetPaymentsAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken = default)
        {
            var payments = await _paymentRepository.GetAsync(isAdmin ? null : userId, cancellationToken);
            return payments.Select(ToDto).ToList();
        }

        public async Task<PaymentTransactionDto> GetPaymentAsync(Guid userId, bool isAdmin, Guid paymentId, CancellationToken cancellationToken = default)
        {
            var payment = await GetExistingAsync(paymentId, cancellationToken);
            if (!isAdmin && payment.UserId != userId)
            {
                throw new NotFoundException("Không tìm thấy giao dịch thanh toán.");
            }

            return ToDto(payment);
        }

        public async Task<PaymentTransactionDto> CreatePaymentAsync(PaymentTransactionRequestDto request, CancellationToken cancellationToken = default)
        {
            var createdAt = DateTime.UtcNow;
            await ValidateAsync(request, Guid.Empty, createdAt, cancellationToken);

            var payment = new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                CreatedAt = createdAt
            };
            ApplyRequest(payment, request);

            await _paymentRepository.AddAsync(payment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(payment);
        }

        public async Task<PaymentTransactionDto> UpdatePaymentAsync(Guid paymentId, PaymentTransactionRequestDto request, CancellationToken cancellationToken = default)
        {
            var payment = await GetExistingAsync(paymentId, cancellationToken);
            await ValidateAsync(request, paymentId, payment.CreatedAt, cancellationToken);

            ApplyRequest(payment, request);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(payment);
        }

        public async Task DeletePaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
        {
            var payment = await GetExistingAsync(paymentId, cancellationToken);

            if (await _paymentRepository.HasInvoiceAsync(paymentId, cancellationToken))
            {
                throw new UserFriendlyException("Giao dịch đã có hóa đơn nên không thể xóa.");
            }

            if (await _paymentRepository.HasSubscriptionAsync(paymentId, cancellationToken))
            {
                throw new UserFriendlyException("Giao dịch đã gắn với một gói đăng ký nên không thể xóa. Hãy đổi trạng thái thay thế.");
            }

            _paymentRepository.Remove(payment);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task<PaymentTransaction> GetExistingAsync(Guid paymentId, CancellationToken cancellationToken)
        {
            return await _paymentRepository.GetByIdAsync(paymentId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy giao dịch thanh toán.");
        }

        private async Task ValidateAsync(PaymentTransactionRequestDto request, Guid excludeId, DateTime createdAt, CancellationToken cancellationToken)
        {
            if (!AllowedGateways.Contains(request.Gateway ?? string.Empty))
            {
                throw new UserFriendlyException("Cổng thanh toán không hợp lệ. Chọn một trong: PayOS, VNPay.");
            }

            var status = string.IsNullOrWhiteSpace(request.Status) ? PendingStatus : request.Status;
            if (!AllowedStatuses.Contains(status))
            {
                throw new UserFriendlyException("Trạng thái không hợp lệ. Chọn một trong: Pending, Succeeded, Failed, Cancelled, Expired.");
            }

            if (request.Amount <= 0)
            {
                throw new UserFriendlyException("Số tiền phải lớn hơn 0.");
            }

            if (request.OrderCode <= 0)
            {
                throw new UserFriendlyException("Mã đơn hàng phải là số dương.");
            }

            if (request.ExpiresAt.UtcDateTime <= createdAt)
            {
                throw new UserFriendlyException("ExpiresAt phải lớn hơn CreatedAt.");
            }

            if (!await _paymentRepository.UserExistsAsync(request.UserId, cancellationToken))
            {
                throw new UserFriendlyException("Người dùng không tồn tại.");
            }

            if (!await _paymentRepository.PlanExistsAsync(request.PlanId, cancellationToken))
            {
                throw new UserFriendlyException("Gói đăng ký không tồn tại.");
            }

            if (await _paymentRepository.OrderCodeExistsAsync(request.OrderCode, excludeId, cancellationToken))
            {
                throw new UserFriendlyException("Mã đơn hàng đã tồn tại.");
            }
        }

        private static void ApplyRequest(PaymentTransaction payment, PaymentTransactionRequestDto request)
        {
            payment.UserId = request.UserId;
            payment.PlanId = request.PlanId;
            payment.OrderCode = request.OrderCode;
            payment.Gateway = request.Gateway;
            payment.Amount = request.Amount;
            payment.Status = string.IsNullOrWhiteSpace(request.Status) ? PendingStatus : request.Status;
            payment.ExpiresAt = request.ExpiresAt.UtcDateTime;
            payment.PaidAt = request.PaidAt?.UtcDateTime;
            payment.WebhookReceivedAt = request.WebhookReceivedAt?.UtcDateTime;
        }

        private static PaymentTransactionDto ToDto(PaymentTransaction payment)
        {
            return new PaymentTransactionDto
            {
                Id = payment.Id,
                UserId = payment.UserId,
                PlanId = payment.PlanId,
                OrderCode = payment.OrderCode,
                Gateway = payment.Gateway,
                Amount = payment.Amount,
                Status = payment.Status,
                CreatedAt = UtcTime.ToApi(payment.CreatedAt),
                ExpiresAt = UtcTime.ToApi(payment.ExpiresAt),
                PaidAt = UtcTime.ToApi(payment.PaidAt),
                WebhookReceivedAt = UtcTime.ToApi(payment.WebhookReceivedAt)
            };
        }
    }
}
