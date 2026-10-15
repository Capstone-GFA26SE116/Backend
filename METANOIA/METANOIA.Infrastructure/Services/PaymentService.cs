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
        private const int MaxInvoiceNumberAttempts = 5;
        private const int InvoicePrefixScan = 20;

        private readonly IPaymentTransactionRepository _payments;
        private readonly IUserSubscriptionRepository _subscriptions;
        private readonly IPaymentRefundRepository _refunds;
        private readonly IPayOSClient _gateway;
        private readonly IInvoiceRepository _invoices;
        private readonly IUserRepository _users;
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
            IInvoiceRepository invoices,
            IUserRepository users,
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
            _invoices = invoices;
            _users = users;
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
            var orphans = await _payments.GetSucceededNeedingFulfillmentIdsAsync(BatchSize, _options.RefundsEnabled, cancellationToken);
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

            foreach (var userId in (await _subscriptions.GetExpiredActiveAsync(now, BatchSize, cancellationToken))
                .Select(s => s.UserId).Distinct().ToList())
            {
                await RefreshUserSubscriptionAsync(userId, cancellationToken);
            }
        }

        public async Task RefreshUserSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            // Kiểm tra nhẹ trước để lần đọc thông tin thường (gói còn hạn) không phải mở transaction
            var active = await _subscriptions.GetActiveByUserAsync(userId, cancellationToken);
            if (active is null || active.EndAt is not { } end || end > now)
            {
                return;
            }

            // Một transaction cho mỗi user: chuyển Expired + kích hoạt gói xếp hàng (hoặc về gói Miễn phí) hoặc cả hai cùng được lưu, hoặc không gì cả
            await _unitOfWork.ExecuteInTransactionAsync(() => ExpireAndReplaceAsync(userId, now, cancellationToken), cancellationToken);
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
                await EnsureFulfillmentAsync(payment, cancellationToken);
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
                await EnsureFulfillmentAsync(payment, cancellationToken);
                return WebhookOutcome.Duplicate;
            }

            if (fromWebhook)
            {
                payment.WebhookReceivedAt = now;
            }

            if (receivedAmount != payment.Amount)
            {
                payment.Status = Failed;
                return await HandleUnexpectedMoneyAsync(payment, receivedAmount,
                    $"Số tiền nhận được ({receivedAmount:N0} VND) khác số tiền của đơn ({payment.Amount:N0} VND).", cancellationToken);
            }

            if (payment.Status is Cancelled or Failed)
            {
                return await HandleUnexpectedMoneyAsync(payment, receivedAmount,
                    $"Tiền về sau khi giao dịch đã ở trạng thái {payment.Status}.", cancellationToken);
            }

            // Pending hoặc Expired (tiền về sát giờ hết hạn): chấp nhận, kích hoạt gói
            payment.Status = Succeeded;
            payment.PaidAt = paidAtUtc ?? now;
            await EnsureFulfillmentAsync(payment, cancellationToken);
            return WebhookOutcome.Processed;
        }

        // Tiền đã về nhưng đơn không hợp lệ (sai số tiền, đã hủy...). Hoàn tiền bật: mở yêu cầu hoàn tiền cho admin.
        // Hoàn tiền tắt: chỉ ghi log lỗi và lưu trạng thái, admin xử lý thủ công (giao dịch có WebhookReceivedAt nhưng không Succeeded)
        private async Task<WebhookOutcome> HandleUnexpectedMoneyAsync(PaymentTransaction payment, long amount, string reason, CancellationToken cancellationToken)
        {
            if (_options.RefundsEnabled)
            {
                await OpenSystemRefundAsync(payment, amount, reason, cancellationToken);
                return WebhookOutcome.RefundOpened;
            }

            _logger.LogError("Giao dịch {PaymentId} nhận {Amount} VND nhưng không hợp lệ ({Reason}). Hoàn tiền đang tắt nên cần xử lý thủ công.",
                payment.Id, amount, reason);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return WebhookOutcome.ReviewRequired;
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

        // Giao dịch Succeeded phải có đủ hai thứ: gói đăng ký (UserSubscription) và hóa đơn (Invoice). Thiếu thứ nào thì tạo thứ đó,
        // có đủ rồi thì không làm gì (idempotent: webhook gửi lại, đối soát chạy lại đều an toàn).
        // Gói và hóa đơn được chuẩn bị rồi lưu bằng MỘT lần SaveChanges, tức là cùng một transaction.
        private async Task EnsureFulfillmentAsync(PaymentTransaction payment, CancellationToken cancellationToken)
        {
            if (payment.Status != Succeeded)
            {
                return;
            }

            var plan = await _payments.GetPlanAsync(payment.PlanId, cancellationToken);
            if (plan is null)
            {
                _logger.LogError("Giao dịch {PaymentId} tham chiếu gói {PlanId} không tồn tại.", payment.Id, payment.PlanId);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }

            // Đã có yêu cầu hoàn tiền thì không cấp gói mới, nhưng hóa đơn của khoản đã thu vẫn phải có
            var subscription = await _subscriptions.GetByPaymentIdAsync(payment.Id, cancellationToken);
            if (subscription is null && !await HasOpenRefundAsync(payment.Id, cancellationToken))
            {
                subscription = await StageSubscriptionAsync(payment, plan, cancellationToken);
            }

            Invoice? invoice = null;
            if (!await _invoices.PaymentHasInvoiceAsync(payment.Id, Guid.Empty, cancellationToken))
            {
                invoice = await StageInvoiceAsync(payment, plan, subscription, cancellationToken);
            }

            await SaveFulfillmentAsync(payment, invoice, cancellationToken);
        }

        // Khi hoàn tiền tắt thì không bao giờ truy vấn bảng PaymentRefund
        private async Task<bool> HasOpenRefundAsync(Guid paymentId, CancellationToken cancellationToken)
        {
            return _options.RefundsEnabled && await _refunds.GetActiveByPaymentAsync(paymentId, cancellationToken) is not null;
        }

        // Chuẩn bị UserSubscription (chưa lưu). Có thể lưu trước việc chuyển gói cũ sang Expired để không vướng index "mỗi user một gói Active"
        private async Task<UserSubscription> StageSubscriptionAsync(PaymentTransaction payment, SubscriptionPlan plan, CancellationToken cancellationToken)
        {
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

            var subscription = new UserSubscription
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
            };
            await _subscriptions.AddAsync(subscription, cancellationToken);
            return subscription;
        }

        // Chuẩn bị Invoice (chưa lưu) từ dữ liệu của giao dịch, gói và người mua. Kỳ hóa đơn = thời hạn của gói vừa cấp
        private async Task<Invoice?> StageInvoiceAsync(
            PaymentTransaction payment, SubscriptionPlan plan, UserSubscription? subscription, CancellationToken cancellationToken)
        {
            var user = await _users.GetByIdAsync(payment.UserId, cancellationToken);
            if (user is null)
            {
                _logger.LogError("Không tạo được hóa đơn cho giao dịch {PaymentId}: không tìm thấy người mua {UserId}.", payment.Id, payment.UserId);
                return null;
            }

            var periodStartUtc = subscription?.StartAt ?? DateTime.UtcNow;
            var periodEndUtc = subscription?.EndAt ?? periodStartUtc.AddDays(plan.DurationDays);
            var zone = ResolveTimeZone(user.TimeZone);
            var periodStart = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(periodStartUtc, DateTimeKind.Utc), zone));
            var periodEnd = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(periodEndUtc, DateTimeKind.Utc), zone));
            if (periodEnd <= periodStart)
            {
                periodEnd = periodStart.AddDays(1);
            }

            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                PaymentTransactionId = payment.Id,
                InvoiceNumber = await NextInvoiceNumberAsync(1, cancellationToken),
                PlanName = Truncate(plan.Name, 50),
                Amount = payment.Amount,
                BillingName = Truncate(user.FullName, 100),
                BillingEmail = Truncate(user.Email, 255),
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                IssuedAt = DateTime.UtcNow
            };
            await _invoices.AddAsync(invoice, cancellationToken);
            return invoice;
        }

        // Số hóa đơn dạng MTN-<năm>-<6 chữ số> tăng dần theo năm. Hai thanh toán cùng lúc có thể ra cùng một số:
        // lúc đó unique index chặn, SaveFulfillmentAsync lấy số mới và lưu lại.
        private async Task<string> NextInvoiceNumberAsync(int attempt, CancellationToken cancellationToken)
        {
            var prefix = $"MTN-{DateTime.UtcNow.Year}-";
            var last = 0;
            foreach (var number in await _invoices.GetRecentInvoiceNumbersAsync(prefix, InvoicePrefixScan, cancellationToken))
            {
                if (int.TryParse(number[prefix.Length..], out var sequence))
                {
                    last = sequence;
                    break;
                }
            }

            return $"{prefix}{last + attempt:D6}";
        }

        // Lưu gói, hóa đơn và trạng thái giao dịch cùng lúc
        private async Task SaveFulfillmentAsync(PaymentTransaction payment, Invoice? invoice, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    return;
                }
                catch (DbUpdateException ex)
                {
                    // Số hóa đơn vừa bị người khác lấy mất: đổi số rồi lưu lại. Thực thể vẫn đang được theo dõi nên lưu lại được toàn bộ
                    if (invoice is not null
                        && attempt < MaxInvoiceNumberAttempts
                        && await _invoices.InvoiceNumberExistsAsync(invoice.InvoiceNumber, invoice.Id, cancellationToken))
                    {
                        invoice.InvoiceNumber = await NextInvoiceNumberAsync(attempt + 1, cancellationToken);
                        continue;
                    }

                    // Còn lại thường là unique(PaymentTransactionId): một luồng khác đã xử lý xong. Lượt đối soát sau sẽ tự bù nếu là lỗi khác
                    _logger.LogWarning(ex, "Không lưu được gói/hóa đơn cho giao dịch {PaymentId}, coi như đã được xử lý bởi luồng khác.", payment.Id);
                    return;
                }
            }
        }

        private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId ?? "Asia/Ho_Chi_Minh");
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return TimeZoneInfo.CreateCustomTimeZone("UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
            }
        }

        private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

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
