using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace METANOIA.Infrastructure.Services
{
    // Lưới an toàn khi webhook không tới (server tắt, mạng yếu, PayOS gọi lỗi): định kỳ hỏi PayOS về các đơn Pending
    // đã cũ, vá các giao dịch Succeeded chưa có gói, và chuyển trạng thái các gói hết hạn / đến hạn kích hoạt.
    public class PaymentReconciliationWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly PaymentOptions _options;
        private readonly ILogger<PaymentReconciliationWorker> _logger;

        public PaymentReconciliationWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<PaymentOptions> options,
            ILogger<PaymentReconciliationWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.ReconciliationEnabled)
            {
                _logger.LogInformation("Đối soát thanh toán nền đang tắt (Payments:ReconciliationEnabled = false).");
                return;
            }

            var interval = TimeSpan.FromSeconds(Math.Max(10, _options.ReconciliationIntervalSeconds));

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    await RunOnceAsync(stoppingToken);
                    await Task.Delay(interval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // tắt ứng dụng
            }
        }

        private async Task RunOnceAsync(CancellationToken stoppingToken)
        {
            IReadOnlyList<Guid> ids;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var payments = scope.ServiceProvider.GetRequiredService<IPaymentService>();
                await payments.RefreshSubscriptionStatesAsync(stoppingToken);
                ids = await payments.GetPaymentIdsNeedingReconciliationAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Đối soát thanh toán: lỗi khi lập danh sách việc cần làm.");
                return;
            }

            foreach (var id in ids)
            {
                // Mỗi giao dịch một scope riêng để lỗi của đơn này không làm bẩn DbContext của đơn khác
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var payments = scope.ServiceProvider.GetRequiredService<IPaymentService>();
                    await payments.ReconcilePaymentAsync(id, stoppingToken);
                }
                catch (PaymentGatewayException ex)
                {
                    // PayOS đang lỗi/mạng yếu: dừng lượt này, phút sau thử lại, không đánh dấu gì cả
                    _logger.LogWarning("Đối soát giao dịch {PaymentId} dừng vì PayOS không phản hồi: {Message}", id, ex.Message);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Đối soát giao dịch {PaymentId} thất bại.", id);
                }
            }
        }
    }
}
