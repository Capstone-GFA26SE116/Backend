namespace METANOIA.Application.Exceptions
{
    public class GoogleCalendarNotConnectedException : Exception
    {
        public GoogleCalendarNotConnectedException()
            : base("Bạn chưa kết nối Google Calendar hoặc quyền truy cập đã bị thu hồi.")
        {
        }
    }
}
