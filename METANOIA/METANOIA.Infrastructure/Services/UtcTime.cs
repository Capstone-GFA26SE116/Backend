namespace METANOIA.Infrastructure.Services
{
    // Cột timestamptz chỉ nhận DateTime có Kind = Utc; khi đọc lên thì trả về DateTimeOffset UTC
    internal static class UtcTime
    {
        public static DateTime ToStorage(DateTimeOffset value) => value.UtcDateTime;

        // Số ngày còn lại tới ngày hết hạn, làm tròn lên (còn 1 giờ vẫn là 1 ngày), không âm. null nếu không có ngày hết hạn (gói Miễn phí)
        public static int? RemainingDays(DateTime? endUtc, DateTime nowUtc) =>
            endUtc is null ? null : (int)Math.Max(0, Math.Ceiling((endUtc.Value - nowUtc).TotalDays));

        public static DateTimeOffset? ToApi(DateTime? value) => value is null ? null : ToApi(value.Value);

        public static DateTimeOffset ToApi(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);
        }
    }
}
