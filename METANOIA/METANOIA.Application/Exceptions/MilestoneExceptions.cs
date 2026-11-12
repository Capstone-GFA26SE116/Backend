namespace METANOIA.Application.Exceptions
{
    // Giao giai đoạn (Delivered) khi còn task chưa Done hoặc Skipped: trả 409 kèm danh sách để người dùng chọn làm tiếp hoặc xác nhận vẫn giao
    public sealed class MilestoneOpenTasksException(string message, object details) : Exception(message)
    {
        public object Details { get; } = details;
    }
}
