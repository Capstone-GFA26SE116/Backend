namespace METANOIA.Infrastructure.Services
{
    // Cột timestamptz chỉ nhận DateTime có Kind = Utc; khi đọc lên thì trả về DateTimeOffset UTC
    internal static class UtcTime
    {
        public static DateTime ToStorage(DateTimeOffset value) => value.UtcDateTime;

        public static DateTimeOffset? ToApi(DateTime? value) => value is null ? null : ToApi(value.Value);

        public static DateTimeOffset ToApi(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);
        }
    }
}
