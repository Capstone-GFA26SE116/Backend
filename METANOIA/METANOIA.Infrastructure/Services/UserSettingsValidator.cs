using METANOIA.Application.Exceptions;

namespace METANOIA.Infrastructure.Services
{
    // Kiểm tra cài đặt của user, theo các CHECK của bảng User. Dùng chung cho PUT /api/me/settings và quản trị user của Admin
    internal static class UserSettingsValidator
    {
        public const int MinMinutes = 0;
        public const int MaxMinutes = 120;

        public static void ValidateWorkHours(TimeOnly start, TimeOnly end)
        {
            if (end <= start)
            {
                throw new UserFriendlyException("Giờ kết thúc làm việc phải lớn hơn giờ bắt đầu.");
            }
        }

        public static void ValidateMinutes(int value, string label)
        {
            if (value < MinMinutes || value > MaxMinutes)
            {
                throw new UserFriendlyException($"{label} phải nằm trong khoảng {MinMinutes} đến {MaxMinutes} phút.");
            }
        }

        public static string ValidateTimeZone(string? timeZone)
        {
            var value = timeZone?.Trim();
            if (string.IsNullOrEmpty(value) || value.Length > 50)
            {
                throw new UserFriendlyException("Múi giờ không hợp lệ (tối đa 50 ký tự).");
            }

            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(value);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new UserFriendlyException($"Múi giờ '{value}' không tồn tại. Ví dụ hợp lệ: Asia/Ho_Chi_Minh.");
            }

            return value;
        }
    }

    internal static class AccountConstants
    {
        // Tài khoản do Admin tạo trước khi chủ nhân đăng nhập Google lần đầu có GoogleSubjectId giữ chỗ bắt đầu bằng tiền tố này.
        // Lần đăng nhập Google đầu tiên bằng đúng email (Google đã xác minh) sẽ nhận tài khoản và thay bằng GoogleSubjectId thật.
        public const string PendingSubjectPrefix = "pending:";
    }
}
