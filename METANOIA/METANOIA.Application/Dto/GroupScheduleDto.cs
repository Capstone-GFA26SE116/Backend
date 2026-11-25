namespace METANOIA.Application.Dto
{
    // Member tự xếp giờ làm cho việc (hoặc việc con) được giao cho mình. Slot dùng chung bảng ScheduleSlot với nhánh cá nhân
    public class GroupSlotRequestDto
    {
        // Bắt buộc khi việc có việc con. Bỏ trống khi xếp cho cả việc không có việc con
        public Guid? SubTaskId { get; set; }

        public DateTimeOffset StartTime { get; set; }

        public DateTimeOffset EndTime { get; set; }

        public string? Explanation { get; set; }

        // Approved (mặc định) hoặc Proposed
        public string? Status { get; set; }
    }

    public class GroupSlotDto : ScheduleSlotDto
    {
        public Guid GroupId { get; set; }

        // Người đã xếp slot
        public string UserName { get; set; } = null!;

        public string TaskTitle { get; set; } = null!;

        public string? SubTaskTitle { get; set; }
    }
}
