using System.Collections.Concurrent;
using System.Text.Json;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Services;

namespace METANOIA.Test.Infrastructure
{
    // PayOS giả để test không gọi mạng thật. Mỗi test điều khiển hành vi bằng các thuộc tính / Info.
    public class FakePayOSClient : IPayOSClient
    {
        public const string ValidSignature = "valid-signature";

        public bool FailOnCreate { get; set; }

        public bool FailOnGetInfo { get; set; }

        public bool FailOnCancel { get; set; }

        public PayOSCancelResult CancelResult { get; set; } = PayOSCancelResult.Cancelled;

        public int CreateCalls { get; private set; }

        public int CancelCalls { get; private set; }

        // Trạng thái "thật" bên PayOS theo orderCode; test chỉnh để mô phỏng khách đã trả tiền / hết hạn...
        public ConcurrentDictionary<long, PayOSPaymentInfo> Info { get; } = new();

        public Task<PayOSPaymentLink> CreatePaymentLinkAsync(PayOSCreateRequest request, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            if (FailOnCreate)
            {
                throw new PaymentGatewayException("Không kết nối được tới cổng thanh toán PayOS.");
            }

            var linkId = $"link-{request.OrderCode}";
            var url = $"https://pay.payos.vn/web/{linkId}";
            Info[request.OrderCode] = new PayOSPaymentInfo("PENDING", request.Amount, 0, linkId, url, "qr", null);
            return Task.FromResult(new PayOSPaymentLink(linkId, url, "qr"));
        }

        public Task<PayOSPaymentInfo?> GetPaymentInfoAsync(long orderCode, CancellationToken cancellationToken = default)
        {
            if (FailOnGetInfo)
            {
                throw new PaymentGatewayException("Không kết nối được tới cổng thanh toán PayOS.");
            }

            return Task.FromResult(Info.TryGetValue(orderCode, out var info) ? info : null);
        }

        public Task<PayOSCancelResult> CancelPaymentLinkAsync(long orderCode, string reason, CancellationToken cancellationToken = default)
        {
            CancelCalls++;
            if (FailOnCancel)
            {
                throw new PaymentGatewayException("Không kết nối được tới cổng thanh toán PayOS.");
            }

            if (CancelResult == PayOSCancelResult.Cancelled && Info.TryGetValue(orderCode, out var info))
            {
                Info[orderCode] = info with { Status = "CANCELLED" };
            }

            return Task.FromResult(CancelResult);
        }

        public Task ConfirmWebhookAsync(string webhookUrl, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        // Body hợp lệ khi signature == ValidSignature
        public PayOSWebhookData? VerifyWebhook(string rawBody)
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            if (root.GetProperty("signature").GetString() != ValidSignature)
            {
                return null;
            }

            var data = root.GetProperty("data");
            return new PayOSWebhookData(
                root.GetProperty("code").GetString() == "00",
                data.GetProperty("orderCode").GetInt64(),
                data.GetProperty("amount").GetInt64(),
                null,
                DateTime.UtcNow);
        }

        public static string WebhookBody(long orderCode, long amount, string signature = ValidSignature, string code = "00")
        {
            return JsonSerializer.Serialize(new
            {
                code,
                desc = "success",
                success = code == "00",
                data = new { orderCode, amount },
                signature
            });
        }
    }
}
