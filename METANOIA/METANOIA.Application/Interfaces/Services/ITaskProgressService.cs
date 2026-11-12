namespace METANOIA.Application.Interfaces.Services
{
    // Kết quả của một lần đồng bộ trạng thái, để người gọi xử lý các việc phải làm SAU khi đã lưu thành công
    public sealed class ProgressResult
    {
        public Guid? TaskId { get; set; }

        // Task vừa chuyển sang Done (tự động hoặc người dùng đặt): báo dịch vụ AI để học
        public bool TaskBecameDone { get; set; }

        // Các slot vừa bị hủy: cần hủy luôn job nhắc việc của chúng
        public List<Guid> CancelledSlotIds { get; } = new();

        public void Merge(ProgressResult other)
        {
            TaskId ??= other.TaskId;
            TaskBecameDone |= other.TaskBecameDone;
            CancelledSlotIds.AddRange(other.CancelledSlotIds);
        }
    }

    // Giữ trạng thái của Task, SubTask, Slot và Milestone khớp với nhau (xem docs/STATUS_WORKFLOW.md).
    // Các hàm chỉ sửa entity đang được theo dõi, KHÔNG tự SaveChanges: người gọi lưu cùng một transaction.
    public interface ITaskProgressService
    {
        // Suy lại trạng thái Task từ sự thật (phiên làm việc, slot, subtask): tự Done khi mọi subtask xong,
        // mở lại khi có subtask ToDo, về InProgress, Scheduled hoặc ToDo; rồi cập nhật Milestone
        Task<ProgressResult> RefreshTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

        // Task vừa bị đóng bằng tay (Done hoặc Skipped): subtask ToDo thành Skipped, slot chưa diễn ra bị hủy
        Task CascadeCloseAsync(Guid taskId, ProgressResult result, CancellationToken cancellationToken = default);

        // Chỉ áp luật của Milestone (Planned tự lên InProgress khi có task bắt đầu làm)
        Task RefreshMilestoneAsync(Guid? milestoneId, CancellationToken cancellationToken = default);

        // Gọi SAU khi đã lưu: hủy job của slot vừa bị hủy và báo AI khi Task vừa Done
        Task AfterCommitAsync(ProgressResult result, CancellationToken cancellationToken = default);
    }
}
