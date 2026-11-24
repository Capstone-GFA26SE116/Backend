namespace METANOIA.Application.Exceptions
{
    // Duyệt đề xuất lịch khi có slot trùng sự kiện Google: trả 409 kèm danh sách để người dùng sửa, từ chối, hoặc chọn bỏ các slot trùng
    public sealed class ScheduleConflictException(string message, object details) : Exception(message)
    {
        public object Details { get; } = details;
    }

    // Tạo, sửa hoặc duyệt slot làm chồng giờ lên slot đang hoạt động khác của chính người dùng: trả 409 SLOT_OVERLAP kèm danh sách slot đang chiếm giờ
    public sealed class SlotOverlapException(string message, object details) : Exception(message)
    {
        public object Details { get; } = details;
    }
}
