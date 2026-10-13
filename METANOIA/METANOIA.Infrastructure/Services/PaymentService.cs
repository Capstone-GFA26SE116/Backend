using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Luồng thanh toán PayOS và các tình huống lỗi. Nguyên tắc xuyên suốt:
    //  - Webhook chỉ là "tín hiệu"; trạng thái thật luôn có thể hỏi lại PayOS (SyncAsync / worker đối soát).
    //  - Không bao giờ đoán trạng thái khi gọi PayOS lỗi mạng: ném PaymentGatewayException và giữ nguyên.
    //  - Tiền đã về mà đơn không hợp lệ thì mở yêu cầu hoàn tiền, không nuốt tiền.
    public class PaymentService : IPaymentService
    {
        private const string Pending = "Pending";
        private const string Succeeded = "Succeeded";
        private const string Failed = "Failed";
        private const string Cancelled = "Cancelled";
        private const string Expired = "Expired";
        private const int BatchSize = 50;

        private readonly IPaymentTransactionRepository _payments;
        private readonly IUserSubscriptionRepository _subscriptions;
        private readonly IPaymentRefundRepository _refunds;
        private readonly IPayOSClient _gateway;
        private readonly IUserInitializationService _userInitializationService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly PaymentOptions _options;
        private readonly PayOSOptions _payOsOptions;
        private readonly ILogger<PaymentService> _logger;

        public PaymentService(
            IPaymentTransactionRepository payments,
            IUserSubscriptionRepository subscriptions,
            IPaymentRefundRepository refunds,
            IPayOSClient gateway,
            IUserInitializationService userInitializationService,
            IUnitOfWork unitOfWork,
            IOptions<PaymentOptions> options,
            IOptions<PayOSOptions> payOsOptions,
            ILogger<PaymentService> logger)
        {
            _payments = payments;
            _subscriptions = subscriptions;
            _refunds = refunds;
            _gateway = gateway;
            _userInitializationService = userInitializationService;
            _unitOfWork = unitOfWork;
            _options = options.Value;
            _payOsOptions = payOsOptions.Value;
            _logger = logger;
        }

        // ---------------------------------------------------------------- Checkout

        public async Task<CheckoutResultDto> CheckoutAsync(Guid userId, Guid planId, CancellationToken cancellationToken = default)
        {
            var plan = await _payments.GetPlanAsync(planId, cancellationToken)
                ?? throw new UserFriendlyException("Gói đăng ký không tồn tại.");
            if (!plan.IsActive)
            {
                throw new UserFriendlyException("Gói này hiện không được bán.");
            }

            if (plan.Price <= 0)
            {
                throw new UserFriendlyException("Gói miễn phí không cần thanh toán.");
            }

            if (!await _payments.UserExistsAsync(userId, cancellationToken))
            {
                throw new NotFoundException("Không tìm thấy người dùng.");
            }

            // Bấm thanh toán hai lần / tải lại trang: dùng lại link còn sống thay vì tạo đơn mới
            var open = await _payments.FindOpenPendingAsync(userId, planId, DateTime.UtcNow, cancellationToken);
            if (open is not null)
            {
                var reused = await TryReuseAsync(open, cancellationToken);
                if (reused is not null)
                {
                    return reused;
                }
            }

            var now = DateTime.UtcNow;
            var payment = new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlanId = planId,
                OrderCode = GenerateOrderCode(),
                Gateway = "PayOS",
                Amount = plan.Price,
                Status = Pending,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(_options.LinkExpiryMinutes)
            };

            // Lưu trước khi gọi PayOS: nếu giữa chừng mất mạng/crash, đơn đã có orderCode để đối soát về sau
            await _payments.AddAsync(payment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            PayOSPaymentLink link;
            try
            {
                link = await _gateway.CreatePaymentLinkAsync(
                    new PayOSCreateRequest(payment.OrderCode, payment.Amount, _payOsOptions.Description, plan.Name, payment.ExpiresAt),
                    cancellationToken);
            }
            catch (PaymentGatewayException)
            {
                // Khách chưa nhận được link nên không thể trả tiền; link mồ côi (nếu PayOS lỡ tạo) tự hết hạn
                payment.Status = Failed;
                await _unitOfWork.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            return new CheckoutResultDto
            {
                PaymentId = payment.Id,
                OrderCode = payment.OrderCode,
                Status = Pending,
                CheckoutUrl = link.CheckoutUrl,
                QrCode = link.QrCode,
                ExpiresAt = UtcTime.ToApi(payment.ExpiresAt)
            };
        }

        private async Task<CheckoutResultDto?> TryReuseAsync(PaymentTransaction open, CancellationToken cancellationToken)
        {
            var info = await _gateway.GetPaymentInfoAsync(open.OrderCode, cancellationToken);
            if (info is null)
            {
                // Lần trước lưu đơn xong nhưng chưa tạo được link ở PayOS
                open.Status = Failed;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return null;
            }

            switch (info.Status)
            {
                case "PENDING":
                case "PROCESSING":
                    return new CheckoutResultDto
                    {
                        PaymentId = open.Id,
                        OrderCode = open.OrderCode,
                        Status = Pending,
                        CheckoutUrl = info.CheckoutUrl ?? BuildCheckoutUrl(info.PaymentLinkId),
                        QrCode = info.QrCode,
                        ExpiresAt = UtcTime.ToApi(open.ExpiresAt),
                        Reused = true
                    };
                default:
                    await ApplyGatewayInfoAsync(open, info, cancellationToken);
                    return open.Status == Succeeded
                        ? new CheckoutResultDto
                        {
                            PaymentId = open.Id,
                            OrderCode = open.OrderCode,
                            Status = Succeeded,
                            ExpiresAt = UtcTime.ToApi(open.ExpiresAt),
                            Reused = true
                        }
                        : null;
            }
        }

        // ---------------------------------------------------------------- Sync / Cancel

        public async Task<PaymentTransactionDto> SyncAsync(Guid userId, bool isAdmin, Guid paymentId, CancellationToken cancellationToken = default)
        {
            var payment = await GetAccessibleAsync(userId, isAdmin, paymentId, cancellationToken);
            await SyncWithGatewayAsync(payment, cancellationToken);
            return ToDto(payment);
        }

        public async Task<PaymentTransactionDto> CancelAsync(Guid userId, bool isAdmin, Guid paymentId, CancellationToken cancellationToken = default)
        {
            var payment = await GetAccessibleAsync(userId, isAdmin, paymentId, cancellationToken);

            switch (payment.Status)
            {
                case Succeeded:
                    throw new UserFriendlyException("Giao dịch đã thanh toán thành công nên không thể hủy. Nếu cần, hãy gửi yêu cầu hoàn tiền.");
                case Pending:
                    break;
                default:
                    // Cancelled / Expired / Failed: đã kết thúc, hủy lại không có tác dụng (idempotent)
                    return ToDto(payment);
            }

            // Lỗi mạng ở đây sẽ ném PaymentGatewayException và giữ nguyên Pending:
            // link PayOS vẫn còn sống nên không được tự tiện đánh dấu Cancelled.
            var result = await _gateway.CancelPaymentLinkAsync(payment.OrderCode, "Người dùng hủy thanh toán", cancellationToken);
            if (result == PayOSCancelResult.NotCancellable)
            {
                // Có thể khách vừa trả tiền xong: hỏi PayOS để biết sự thật
                await SyncWithGatewayAsync(payment, cancellationToken);
                if (payment.Status == Pending)
                {
                    throw new UserFriendlyException("PayOS chưa cho hủy giao dịch này. Vui lòng thử lại sau ít phút.");
                }

                return ToDto(payment);
            }

            payment.Status = Cancelled;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(payment);
        }

        // ---------------------------------------------------------------- Webhook

        public async Task<WebhookOutcome> HandleWebhookAsync(string rawBody, CancellationToken cancellationToken = default)
        {
            var data = _gateway.VerifyWebhook(rawBody);
            if (data is null)
            {
                return WebhookOutcome.InvalidSignature;
            }

            if (!data.Success)
            {
                _logger.LogInformation("Webhook PayOS báo không thành công cho orderCode {OrderCode}, bỏ qua.", data.OrderCode);
                return WebhookOutcome.Ignored;
            }

            var payment = await _payments.GetByOrderCodeAsync(data.OrderCode, cancellationToken);
            if (payment is null)
            {
                // Gồm cả webhook thử của PayOS khi đăng ký URL (orderCode mẫu): vẫn trả 200 để không bị gọi lại
                _logger.LogWarning("Webhook PayOS cho orderCode {OrderCode} không có trong hệ thống.", data.OrderCode);
                return WebhookOutcome.Ignored;
            }

            return await ApplyPaymentReceivedAsync(payment, data.Amount, data.PaidAtUtc, fromWebhook: true, cancellationToken);
        }

        public async Task ConfirmWebhookAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_payOsOptions.WebhookUrl) || _payOsOptions.WebhookUrl.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase)
                || _payOsOptions.WebhookUrl.Contains("YOUR_PUBLIC_DOMAIN", StringComparison.OrdinalIgnoreCase))
            {
                throw new UserFriendlyException("Chưa cấu hình PayOS:WebhookUrl (phải là URL công khai, không phải localhost).");
            }

            await _gateway.ConfirmWebhookAsync(_payOsOptions.WebhookUrl, cancellationToken);
        }

        // ---------------------------------------------------------------- Reconciliation

        public async Task<IReadOnlyList<Guid>> GetPaymentIdsNeedingReconciliationAsync(CancellationToken cancellationToken = default)
        {
            var staleBefore = DateTime.UtcNow.AddSeconds(-_options.PendingStaleAfterSeconds);
            var pending = await _payments.GetStalePendingIdsAsync(staleBefore, BatchSize, cancellationToken);
            var orphans = await _payments.GetSucceededWithoutSubscriptionIdsAsync(BatchSize, cancellationToken);
            return pending.Concat(orphans).Distinct().ToList();
        }

        public async Task ReconcilePaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
        {
            var payment = await _payments.GetByIdAsync(paymentId, cancellationToken);
            if (payment is not null)
            {
                await SyncWithGatewayAsync(payment, cancellationToken);
            }
        }

        public async Task RefreshSubscriptionStatesAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            // Mỗi user một transaction: chuyển Expired + kích hoạt gói xếp hàng (hoặc về gói Miễn phí) hoặc cả hai cùng được lưu, hoặc không gì cả
            foreach (var userId in (await _subscriptions.GetExpiredActiveAsync(now, BatchSize, cancellationToken))
                .Select(s => s.UserId).Distinct().ToList())
            {
                await _unitOfWork.ExecuteInTransactionAsync(
                    () => ExpireAndReplaceAsync(userId, now, cancellationToken), cancellationToken);
            }
        }

        private async Task ExpireAndReplaceAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
        {
            // Lưu Expired trước để index "mỗi user một gói Active" không chặn việc kích hoạt gói thay thế
            var active = await _subscriptions.GetActiveByUserAsync(userId, cancellationToken);
            if (active is null || active.EndAt is not { } end || end > now)
            {
                return;
            }

            active.Status = Expired;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var queued = await _subscriptions.GetDueScheduledForUserAsync(userId, now, cancellationToken);
            if (queued is not null)
            {
                queued.Status = "Active";
                queued.QuotaResetAt = now;
            }
            else
            {
                // Không còn gói Active nào khác: quay về gói Miễn phí
                await _userInitializationService.StageFreeSubscriptionAsync(userId, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // ---------------------------------------------------------------- Core

        private async Task SyncWithGatewayAsync(PaymentTransaction payment, CancellationToken cancellationToken)
        {
            if (payment.Status == Succeeded)
            {
                await EnsureSubscriptionAsync(payment, cancellationToken);
                return;
            }

            if (payment.Status is Cancelled or Failed)
            {
                return;
            }

            var info = await _gateway.GetPaymentInfoAsync(payment.OrderCode, cancellationToken);
            if (info is null)
            {
                // PayOS không biết đơn này: link chưa từng được tạo thành công
                if (payment.Status == Pending && DateTime.UtcNow > payment.ExpiresAt)
                {
                    payment.Status = Expired;
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                return;
            }

            await ApplyGatewayInfoAsync(payment, info, cancellationToken);
        }

        private async Task ApplyGatewayInfoAsync(PaymentTransaction payment, PayOSPaymentInfo info, CancellationToken cancellationToken)
        {
            switch (info.Status)
            {
                case "PAID":
                case "UNDERPAID":
                    var received = info.AmountPaid > 0 ? info.AmountPaid : info.Amount;
                    await ApplyPaymentReceivedAsync(payment, received, info.PaidAtUtc, fromWebhook: false, cancellationToken);
                    break;

                case "CANCELLED":
                    if (payment.Status == Pending)
                    {
                        payment.Status = Cancelled;
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                    }

                    break;

                case "EXPIRED":
                    if (payment.Status == Pending)
                    {
                        payment.Status = Expired;
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                    }

                    break;

                default:
                    // PENDING / PROCESSING mà đã quá hạn: đóng link để khách không trả tiền vào đơn đã bị coi là hết hạn
                    if (payment.Status == Pending && DateTime.UtcNow > payment.ExpiresAt.AddSeconds(_options.ExpiryGraceSeconds))
                    {
                        var result = await _gateway.CancelPaymentLinkAsync(payment.OrderCode, "Hết hạn thanh toán", cancellationToken);
                        if (result != PayOSCancelResult.NotCancellable)
                        {
                            payment.Status = Expired;
                            await _unitOfWork.SaveChangesAsync(cancellationToken);
                        }
                        // NotCancellable: có thể vừa được trả tiền, lượt đối soát sau sẽ thấy PAID
                    }

                    break;
            }
        }

        // Tiền đã về cho `payment`. Idempotent: webhook gửi lại / đối soát chạy lại không kích hoạt hai lần.
        private async Task<WebhookOutcome> ApplyPaymentReceivedAsync(
            PaymentTransaction payment, long receivedAmount, DateTime? paidAtUtc, bool fromWebhook, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            if (payment.Status == Succeeded)
            {
                await EnsureSubscriptionAsync(payment, cancellationToken);
                return WebhookOutcome.Duplicate;
            }

            if (fromWebhook)
            {
                payment.WebhookReceivedAt = now;
            }

            if (receivedAmount != payment.Amount)
            {
                payment.Status = Failed;
                await OpenSystemRefundAsync(payment, receivedAmount,
                    $"Số tiền nhận được ({receivedAmount:N0} VND) khác số tiền của đơn ({payment.Amount:N0} VND).", cancellationToken);
                return WebhookOutcome.RefundOpened;
            }

            if (payment.Status is Cancelled or Failed)
            {
                await OpenSystemRefundAsync(payment, receivedAmount,
                    $"Tiền về sau khi giao dịch đã ở trạng thái {payment.Status}.", cancellationToken);
                return WebhookOutcome.RefundOpened;
            }

            // Pending hoặc Expired (tiền về sát giờ hết hạn): chấp nhận, kích hoạt gói
            payment.Status = Succeeded;
            payment.PaidAt = paidAtUtc ?? now;
            await EnsureSubscriptionAsync(payment, cancellationToken);
            return WebhookOutcome.Processed;
        }

        private async Task OpenSystemRefundAsync(PaymentTransaction payment, long amount, string reason, CancellationToken cancellationToken)
        {
            if (await _refunds.GetActiveByPaymentAsync(payment.Id, cancellationToken) is null)
            {
                await _refunds.AddAsync(new PaymentRefund
                {
                    Id = Guid.NewGuid(),
                    PaymentTransactionId = payment.Id,
                    Amount = amount,
                    Reason = reason,
                    Source = "System",
                    Status = "Requested",
                    CreatedAt = DateTime.UtcNow
                }, cancellationToken);
                _logger.LogWarning("Mở yêu cầu hoàn tiền {Amount} cho giao dịch {PaymentId}: {Reason}", amount, payment.Id, reason);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // Tạo gói đăng ký cho giao dịch Succeeded nếu chưa có. Lỗi trùng khóa (webhook song song) coi như đã có người làm.
        private async Task EnsureSubscriptionAsync(PaymentTransaction payment, CancellationToken cancellationToken)
        {
            if (payment.Status != Succeeded)
            {
                return;
            }

            if (await _subscriptions.GetByPaymentIdAsync(payment.Id, cancellationToken) is not null
                || await _refunds.GetActiveByPaymentAsync(payment.Id, cancellationToken) is not null)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }

            var plan = await _payments.GetPlanAsync(payment.PlanId, cancellationToken);
            if (plan is null)
            {
                _logger.LogError("Giao dịch {PaymentId} tham chiếu gói {PlanId} không tồn tại.", payment.Id, payment.PlanId);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }

            var now = DateTime.UtcNow;
            var start = now;
            var status = "Active";

            var active = await _subscriptions.GetActiveByUserAsync(payment.UserId, cancellationToken);
            if (active is not null)
            {
                if (active.EndAt is { } end && end > now)
                {
                    // Đang còn hạn: gói mới xếp hàng, tự chạy khi gói cũ hết hạn (RefreshSubscriptionStatesAsync)
                    start = end;
                    status = "Scheduled";
                }
                else
                {
                    active.Status = Expired;
                    active.EndAt ??= now;
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
            }

            await _subscriptions.AddAsync(new UserSubscription
            {
                Id = Guid.NewGuid(),
                UserId = payment.UserId,
                PlanId = payment.PlanId,
                PaymentTransactionId = payment.Id,
                StartAt = start,
                EndAt = start.AddDays(plan.DurationDays),
                Status = status,
                AiCallsUsed = 0,
                QuotaResetAt = start,
                CreatedAt = now
            }, cancellationToken);

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Unique(PaymentTransactionId): một webhook khác đã kích hoạt trước. Lượt đối soát sau sẽ tự sửa nếu là lỗi khác.
                _logger.LogWarning(ex, "Không lưu được gói cho giao dịch {PaymentId}, coi như đã được xử lý bởi luồng khác.", payment.Id);
            }
        }

        private async Task<PaymentTransaction> GetAccessibleAsync(Guid userId, bool isAdmin, Guid paymentId, CancellationToken cancellationToken)
        {
            var payment = await _payments.GetByIdAsync(paymentId, cancellationToken);
            if (payment is null || (!isAdmin && payment.UserId != userId))
            {
                throw new NotFoundException("Không tìm thấy giao dịch thanh toán.");
            }

            return payment;
        }

        // orderCode phải là số nguyên an toàn của JavaScript (< 2^53): mili-giây * 1000 + 3 chữ số ngẫu nhiên ~ 1.8e15
        private static long GenerateOrderCode()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000 + Random.Shared.Next(0, 1000);
        }

        private static string? BuildCheckoutUrl(string? paymentLinkId)
        {
            return string.IsNullOrWhiteSpace(paymentLinkId) ? null : $"https://pay.payos.vn/web/{paymentLinkId}";
        }

        private static PaymentTransactionDto ToDto(PaymentTransaction payment) => PaymentTransactionService.ToDto(payment);
    }
}
