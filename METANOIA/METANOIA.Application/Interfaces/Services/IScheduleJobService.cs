using METANOIA.Domain.Entities;

namespace METANOIA.Application.Interfaces.Services
{
    // Hẹn giờ các việc chạy nền cho một slot (nhắc, đổi trạng thái, tự ngắt). Là việc "cố gắng hết sức":
    // dữ liệu trong database mới là nguồn sự thật, nên lỗi ở tầng hẹn giờ chỉ được ghi log, không làm hỏng request.
    public interface IScheduleJobService
    {
        // Hủy mọi job cũ của slot rồi hẹn lại toàn bộ theo StartTime và EndTime hiện tại. Gọi khi slot được duyệt hoặc đổi giờ
        void ScheduleAllJobsForSlot(ScheduleSlot slot);

        // Người dùng bấm Start: hủy job cũ và chỉ hẹn lại các job phía cuối (nhắc sắp hết giờ, hết giờ, quá giờ, tự ngắt)
        // tính từ lúc bắt đầu THỰC TẾ. Không sửa StartTime và EndTime của slot.
        void RescheduleSlotJobs(Guid slotId, DateTimeOffset actualStartTime);

        // Hủy các job liên quan tới việc quá giờ: NearEndReminder, OverrunReminder, AutoTimeout
        void CancelOverrunJobs(Guid slotId);

        void CancelAllJobs(Guid slotId);
    }
}
