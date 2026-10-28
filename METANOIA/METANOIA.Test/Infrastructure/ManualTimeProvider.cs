namespace METANOIA.Test.Infrastructure
{
    // Đồng hồ chỉnh tay cho test: đứng yên cho tới khi gọi Advance, nên kiểm tra được "chạy 25 phút" mà không phải chờ
    public sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;

        public void Set(DateTimeOffset value) => _now = value;
    }
}
