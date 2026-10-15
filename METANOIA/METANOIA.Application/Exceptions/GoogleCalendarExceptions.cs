namespace METANOIA.Application.Exceptions
{
    // Kết nối Google Calendar của user không còn dùng được (hết hạn, bị thu hồi quyền, hoặc không giải mã được token đã lưu):
    // user phải kết nối lại qua GET /api/google-calendar/connect (HTTP 409)
    public class GoogleReconnectRequiredException : Exception
    {
        public GoogleReconnectRequiredException() : base("Cần kết nối lại Google Calendar.")
        {
        }
    }

    // Google (API Calendar hoặc máy chủ token) lỗi, quá hạn hoặc không liên lạc được: lỗi tạm thời, thử lại sau (HTTP 502).
    // Message chỉ dùng nội bộ (log, LastSyncError); phản hồi cho người dùng luôn là nội dung chung, không lộ chi tiết.
    public class GoogleApiFailureException : Exception
    {
        public GoogleApiFailureException(string message) : base(message)
        {
        }

        public GoogleApiFailureException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
