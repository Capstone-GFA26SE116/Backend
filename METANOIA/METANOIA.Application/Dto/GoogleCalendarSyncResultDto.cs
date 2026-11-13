namespace METANOIA.Application.Dto
{
    public class GoogleCalendarSyncResultDto
    {
        // Kéo từ Google về METANOIA
        public int Added { get; set; }

        public int Updated { get; set; }

        public int Cancelled { get; set; }

        // Đẩy slot từ METANOIA lên Google
        public int PushedCreated { get; set; }

        public int PushedUpdated { get; set; }

        // Xóa sự kiện trên Google vì slot đã bị hủy, bỏ qua hoặc hoãn
        public int PushedDeleted { get; set; }

        public int PushFailed { get; set; }

        // Tối đa 5 thông báo lỗi đầu tiên của việc đẩy (slot nào, lỗi gì)
        public List<string> PushErrors { get; set; } = new();

        public GoogleSyncDirection Direction { get; set; } = GoogleSyncDirection.FromGoogle;

        // Incremental: kéo theo syncToken. Range: có chọn ngày, lấy đầy đủ trong khoảng đó
        public string Mode { get; set; } = "Incremental";

        // Khoảng thời gian thực tế đã dùng (UTC). null nghĩa là không giới hạn phía đó
        public DateTimeOffset? RangeFromUtc { get; set; }

        public DateTimeOffset? RangeToUtc { get; set; }
    }
}
