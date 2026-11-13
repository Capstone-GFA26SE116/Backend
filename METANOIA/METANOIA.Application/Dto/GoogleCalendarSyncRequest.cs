namespace METANOIA.Application.Dto
{
    // Hướng đồng bộ Google Calendar
    public enum GoogleSyncDirection
    {
        // Kéo sự kiện từ Google về METANOIA (hành vi cũ, mặc định)
        FromGoogle = 0,

        // Đẩy slot đã duyệt và đang làm của METANOIA lên Google
        ToGoogle = 1,

        // Kéo về trước, rồi đẩy lên
        Both = 2
    }

    // Tùy chọn của POST /api/google-calendar/sync.
    // From và To là NGÀY theo múi giờ của người dùng (cài đặt PUT /api/me/settings), cả hai đều gồm trọn ngày đó.
    //  - Không chọn From và To: đồng bộ toàn bộ (kéo về thì theo syncToken, chỉ lấy phần thay đổi).
    //  - Chỉ chọn From: từ đầu ngày From đến hết mọi lịch về sau.
    //  - Chỉ chọn To: từ đầu đến hết ngày To.
    public class GoogleCalendarSyncRequest
    {
        public DateOnly? From { get; set; }

        public DateOnly? To { get; set; }

        public GoogleSyncDirection Direction { get; set; } = GoogleSyncDirection.FromGoogle;
    }
}
