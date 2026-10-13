using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace METANOIA.Infrastructure.Services
{
    public class GoogleCalendarWebhookService : IGoogleCalendarWebhookService
    {
        private const string SyncState = "sync";

        private readonly IGoogleConnectionRepository _connectionRepository;
        private readonly IGoogleCalendarSyncService _syncService;
        private readonly GoogleSyncGate _gate;
        private readonly string _secret;
        private readonly ILogger<GoogleCalendarWebhookService> _logger;

        public GoogleCalendarWebhookService(
            IGoogleConnectionRepository connectionRepository,
            IGoogleCalendarSyncService syncService,
            GoogleSyncGate gate,
            IConfiguration configuration,
            ILogger<GoogleCalendarWebhookService> logger)
        {
            _connectionRepository = connectionRepository;
            _syncService = syncService;
            _gate = gate;
            _logger = logger;
            _secret = configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("Cấu hình 'Jwt:Key' chưa được thiết lập.");
        }

        public async Task<GoogleWebhookResult> HandleAsync(string? channelId, string? token, string? resourceState, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(channelId) || !GoogleWebhookToken.IsValid(channelId, token, _secret))
            {
                _logger.LogWarning("Webhook Google Calendar bị từ chối: thiếu hoặc sai token (kênh {ChannelId}).", channelId);
                return GoogleWebhookResult.Unauthorized;
            }

            // Tin "sync" chỉ báo kênh vừa được tạo, chưa có thay đổi nào
            if (string.Equals(resourceState, SyncState, StringComparison.OrdinalIgnoreCase))
            {
                return GoogleWebhookResult.Ignored;
            }

            var connection = await _connectionRepository.GetByWatchChannelIdAsync(channelId, cancellationToken);
            if (connection is null)
            {
                // Kênh cũ của user đã hủy liên kết hoặc đã đổi kênh mới
                _logger.LogInformation("Webhook Google Calendar cho kênh {ChannelId} không còn thuộc user nào, bỏ qua.", channelId);
                return GoogleWebhookResult.Ignored;
            }

            try
            {
                await _gate.RunAsync(connection.UserId, () => _syncService.SyncAsync(connection.UserId, cancellationToken));
                return GoogleWebhookResult.Processed;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Vẫn trả 200 để Google không gửi lại dồn dập; lần đồng bộ thủ công hoặc thông báo sau sẽ bù
                _logger.LogWarning(ex, "Đồng bộ theo webhook Google Calendar thất bại cho user {UserId}.", connection.UserId);
                return GoogleWebhookResult.Ignored;
            }
        }
    }
}
