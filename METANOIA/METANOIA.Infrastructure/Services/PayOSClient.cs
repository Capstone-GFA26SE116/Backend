using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace METANOIA.Infrastructure.Services
{
    // Gọi PayOS Merchant API trực tiếp bằng HttpClient (không dùng SDK).
    // Mọi lỗi mạng / quá hạn / 5xx / 429 / 401 đều quy về PaymentGatewayException để tầng trên không đoán trạng thái.
    public class PayOSClient : IPayOSClient
    {
        private const string NetworkMessage = "Không kết nối được tới cổng thanh toán PayOS. Vui lòng thử lại sau ít phút.";

        private readonly HttpClient _http;
        private readonly PayOSOptions _options;
        private readonly ILogger<PayOSClient> _logger;

        public PayOSClient(HttpClient http, IOptions<PayOSOptions> options, ILogger<PayOSClient> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<PayOSPaymentLink> CreatePaymentLinkAsync(PayOSCreateRequest request, CancellationToken cancellationToken = default)
        {
            var description = Truncate(_options.Description, 25);
            var signature = Sign(
                $"amount={request.Amount}&cancelUrl={_options.CancelUrl}&description={description}" +
                $"&orderCode={request.OrderCode}&returnUrl={_options.ReturnUrl}");

            var body = new
            {
                orderCode = request.OrderCode,
                amount = request.Amount,
                description,
                items = new[] { new { name = Truncate(request.ItemName, 50), quantity = 1, price = request.Amount } },
                cancelUrl = _options.CancelUrl,
                returnUrl = _options.ReturnUrl,
                expiredAt = new DateTimeOffset(DateTime.SpecifyKind(request.ExpiresAtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds(),
                signature
            };

            var root = await SendAsync(HttpMethod.Post, "/v2/payment-requests", body, cancellationToken);
            var code = GetString(root, "code");
            if (code != "00" || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                _logger.LogError("PayOS từ chối tạo link thanh toán: code={Code}, desc={Desc}", code, GetString(root, "desc"));
                throw new PaymentGatewayException($"PayOS không tạo được link thanh toán: {GetString(root, "desc") ?? "không rõ lý do"}.");
            }

            return new PayOSPaymentLink(
                GetString(data, "paymentLinkId") ?? "",
                GetString(data, "checkoutUrl") ?? "",
                GetString(data, "qrCode"));
        }

        public async Task<PayOSPaymentInfo?> GetPaymentInfoAsync(long orderCode, CancellationToken cancellationToken = default)
        {
            var root = await SendAsync(HttpMethod.Get, $"/v2/payment-requests/{orderCode}", null, cancellationToken);
            var code = GetString(root, "code");
            if (code != "00" || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                if (IsNotFound(GetString(root, "desc")))
                {
                    return null;
                }

                _logger.LogError("PayOS trả lỗi khi tra cứu đơn {OrderCode}: code={Code}, desc={Desc}", orderCode, code, GetString(root, "desc"));
                throw new PaymentGatewayException("Không tra cứu được trạng thái thanh toán từ PayOS. Vui lòng thử lại sau.");
            }

            var paymentLinkId = GetString(data, "id") ?? GetString(data, "paymentLinkId");
            return new PayOSPaymentInfo(
                (GetString(data, "status") ?? "PENDING").ToUpperInvariant(),
                GetLong(data, "amount"),
                GetLong(data, "amountPaid"),
                paymentLinkId,
                GetString(data, "checkoutUrl"),
                GetString(data, "qrCode"),
                FindPaidTime(data));
        }

        public async Task<PayOSCancelResult> CancelPaymentLinkAsync(long orderCode, string reason, CancellationToken cancellationToken = default)
        {
            var root = await SendAsync(HttpMethod.Post, $"/v2/payment-requests/{orderCode}/cancel",
                new { cancellationReason = Truncate(reason, 100) }, cancellationToken);

            if (GetString(root, "code") == "00")
            {
                return PayOSCancelResult.Cancelled;
            }

            var desc = GetString(root, "desc");
            _logger.LogWarning("PayOS không hủy được đơn {OrderCode}: {Desc}", orderCode, desc);
            return IsNotFound(desc) ? PayOSCancelResult.NotFound : PayOSCancelResult.NotCancellable;
        }

        public async Task ConfirmWebhookAsync(string webhookUrl, CancellationToken cancellationToken = default)
        {
            var root = await SendAsync(HttpMethod.Post, "/confirm-webhook", new { webhookUrl }, cancellationToken);
            if (GetString(root, "code") != "00")
            {
                throw new PaymentGatewayException($"PayOS không xác nhận được webhook: {GetString(root, "desc") ?? "không rõ lý do"}.");
            }
        }

        // Chữ ký webhook: sắp xếp các key của `data` theo thứ tự chữ cái, nối thành key=value&key=value
        // (null -> chuỗi rỗng), rồi HMAC-SHA256 bằng ChecksumKey, so với trường `signature` ở ngoài cùng.
        public PayOSWebhookData? VerifyWebhook(string rawBody)
        {
            if (string.IsNullOrWhiteSpace(_options.ChecksumKey))
            {
                _logger.LogError("Chưa cấu hình PayOS:ChecksumKey nên không thể xác thực webhook.");
                return null;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(rawBody);
            }
            catch (JsonException)
            {
                return null;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                    || GetString(root, "signature") is not { Length: > 0 } signature)
                {
                    return null;
                }

                var payload = string.Join("&", data.EnumerateObject()
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => $"{p.Name}={ToSignatureValue(p.Value)}"));

                var expected = Encoding.ASCII.GetBytes(Sign(payload));
                var actual = Encoding.ASCII.GetBytes(signature.ToLowerInvariant());
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    _logger.LogWarning("Webhook PayOS có chữ ký không khớp.");
                    return null;
                }

                var success = GetString(root, "code") == "00"
                    && (!root.TryGetProperty("success", out var flag) || flag.ValueKind != JsonValueKind.False);

                return new PayOSWebhookData(
                    success,
                    GetLong(data, "orderCode"),
                    GetLong(data, "amount"),
                    GetString(data, "paymentLinkId"),
                    ParseGatewayTime(GetString(data, "transactionDateTime")));
            }
        }

        private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            if (!_options.IsConfigured)
            {
                _logger.LogError("PayOS chưa được cấu hình đầy đủ trong appsettings (mục PayOS).");
                throw new PaymentGatewayException("Cổng thanh toán PayOS chưa được cấu hình.");
            }

            using var request = new HttpRequestMessage(method, new Uri(new Uri(_options.ApiBaseUrl), path));
            request.Headers.Add("x-client-id", _options.ClientId);
            request.Headers.Add("x-api-key", _options.ApiKey);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Lỗi mạng khi gọi PayOS {Path}", path);
                throw new PaymentGatewayException(NetworkMessage, ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Quá thời gian chờ khi gọi PayOS {Path}", path);
                throw new PaymentGatewayException(NetworkMessage, ex);
            }

            using (response)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    _logger.LogError("PayOS từ chối xác thực ({Status}). Kiểm tra PayOS:ClientId / ApiKey.", (int)response.StatusCode);
                    throw new PaymentGatewayException("PayOS từ chối thông tin xác thực của hệ thống. Vui lòng liên hệ quản trị viên.");
                }

                if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("PayOS tạm thời không phục vụ ({Status}) tại {Path}", (int)response.StatusCode, path);
                    throw new PaymentGatewayException("Cổng thanh toán PayOS đang bận. Vui lòng thử lại sau ít phút.");
                }

                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                try
                {
                    using var document = JsonDocument.Parse(text);
                    return document.RootElement.Clone();
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "PayOS trả dữ liệu không phải JSON tại {Path}: {Body}", path, Truncate(text, 200));
                    throw new PaymentGatewayException("Cổng thanh toán PayOS trả dữ liệu không hợp lệ.", ex);
                }
            }
        }

        private string Sign(string payload)
        {
            var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.ChecksumKey), Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string ToSignatureValue(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                JsonValueKind.String => value.GetString() is { } s && s is not ("null" or "undefined") ? s : "",
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => value.GetRawText()
            };
        }

        private static bool IsNotFound(string? desc)
        {
            return desc is not null && desc.Contains("không tồn tại", StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetString(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out var value)
                && value.ValueKind != JsonValueKind.Null
                    ? (value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText())
                    : null;
        }

        private static long GetLong(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
                ? number
                : 0;
        }

        private static DateTime? FindPaidTime(JsonElement data)
        {
            if (data.TryGetProperty("transactions", out var transactions) && transactions.ValueKind == JsonValueKind.Array)
            {
                foreach (var transaction in transactions.EnumerateArray())
                {
                    if (ParseGatewayTime(GetString(transaction, "transactionDateTime")) is { } time)
                    {
                        return time;
                    }
                }
            }

            return null;
        }

        // PayOS gửi giờ Việt Nam (UTC+7) dạng "yyyy-MM-dd HH:mm:ss"; chuyển về UTC để lưu timestamptz
        private static DateTime? ParseGatewayTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            {
                return DateTime.SpecifyKind(local.AddHours(-7), DateTimeKind.Utc);
            }

            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offset)
                ? offset.UtcDateTime
                : null;
        }

        private static string Truncate(string value, int max)
        {
            return value.Length <= max ? value : value[..max];
        }
    }
}
