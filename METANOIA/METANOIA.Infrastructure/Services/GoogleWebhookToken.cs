using System.Security.Cryptography;
using System.Text;

namespace METANOIA.Infrastructure.Services
{
    // Token gắn vào kênh Watch khi tạo (Google gửi lại ở header X-Goog-Channel-Token mỗi lần báo).
    // Tính bằng HMAC-SHA256(channelId) với khóa bí mật của server nên không cần lưu thêm gì trong database
    // và kẻ ngoài không thể giả mạo webhook của một kênh.
    public static class GoogleWebhookToken
    {
        public static string Create(string channelId, string secret)
        {
            var key = Encoding.UTF8.GetBytes("google-calendar-webhook:" + secret);
            return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(channelId))).ToLowerInvariant();
        }

        public static bool IsValid(string channelId, string? token, string secret)
        {
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            var expected = Encoding.ASCII.GetBytes(Create(channelId, secret));
            var actual = Encoding.ASCII.GetBytes(token);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
    }
}
