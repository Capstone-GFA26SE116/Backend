using METANOIA.Domain.Enums;

namespace METANOIA.Application.Dto
{
    // ---------------------------------------------------------------- Bấm giờ làm việc của nhóm
    public class GroupFocusStartRequestDto
    {
        // Bắt buộc khi việc có việc con (giờ làm tính cho việc con đó). Bỏ trống khi bấm giờ cho cả việc không có việc con
        public Guid? SubTaskId { get; set; }

        // Tùy chọn: bấm giờ theo một slot đã xếp (slot của mình, đúng việc và việc con). Slot sang InProgress; Stop đổi slot theo lý do dừng
        public Guid? ScheduleSlotId { get; set; }
    }

    public class GroupFocusStopRequestDto
    {
        public FocusStopReason StopReason { get; set; }

        // Bắt buộc khi StopReason là Blocked hoặc Other
        public string? Note { get; set; }
    }

    public class GroupFocusStartResponseDto
    {
        public Guid SessionId { get; set; }

        public Guid GroupTaskId { get; set; }

        public Guid? SubTaskId { get; set; }

        public DateTimeOffset StartedAt { get; set; }

        // Status sau khi bắt đầu: việc con ToDo thành InProgress, việc cha tự suy lại
        public string TaskStatus { get; set; } = null!;

        public string? SubTaskStatus { get; set; }

        public Guid? ScheduleSlotId { get; set; }
    }

    public class GroupFocusStopResponseDto
    {
        public Guid SessionId { get; set; }

        public DateTimeOffset EndedAt { get; set; }

        public double SessionMinutes { get; set; }

        // Tổng giờ của chính người bấm trên việc này và tổng của cả nhóm trên việc này
        public double MyTaskMinutes { get; set; }

        public double TaskTotalMinutes { get; set; }

        public string TaskStatus { get; set; } = null!;

        public string? SubTaskStatus { get; set; }

        // True khi phiên bị cắt theo giới hạn độ dài (quên bấm Stop)
        public bool WasCapped { get; set; }
    }

    public class GroupFocusSessionDto
    {
        public Guid Id { get; set; }

        public Guid? ScheduleSlotId { get; set; }

        public Guid GroupTaskId { get; set; }

        public Guid? SubTaskId { get; set; }

        public Guid UserId { get; set; }

        public string UserName { get; set; } = null!;

        public DateTimeOffset StartedAt { get; set; }

        public DateTimeOffset? EndedAt { get; set; }

        // Phiên đang chạy: tính tới giờ hiện tại
        public double Minutes { get; set; }

        public bool IsRunning { get; set; }

        public string? StopReason { get; set; }

        public string? Note { get; set; }
    }

    public class GroupFocusCurrentDto
    {
        public Guid SessionId { get; set; }

        public Guid GroupId { get; set; }

        public string GroupName { get; set; } = null!;

        public Guid GroupTaskId { get; set; }

        public string TaskTitle { get; set; } = null!;

        public Guid? SubTaskId { get; set; }

        public string? SubTaskTitle { get; set; }

        public DateTimeOffset StartedAt { get; set; }

        public long ElapsedSeconds { get; set; }

        public DateTimeOffset ServerTime { get; set; }

        // Ước lượng của việc, để FE hiện "đã làm / ước lượng"
        public int? EstimatedMinutes { get; set; }

        public Guid? ScheduleSlotId { get; set; }
    }

    // Giờ làm thực tế của một việc, chia theo từng người và từng việc con
    public class GroupTaskTimeDto
    {
        public Guid GroupTaskId { get; set; }

        public int? EstimatedMinutes { get; set; }

        public double TotalMinutes { get; set; }

        public int SessionCount { get; set; }

        public List<GroupTimeByUserDto> ByUser { get; set; } = new();

        public List<GroupTimeBySubTaskDto> BySubTask { get; set; } = new();

        public List<GroupFocusSessionDto> Sessions { get; set; } = new();
    }

    public class GroupTimeByUserDto
    {
        public Guid UserId { get; set; }

        public string FullName { get; set; } = null!;

        public double Minutes { get; set; }

        public int SessionCount { get; set; }
    }

    public class GroupTimeBySubTaskDto
    {
        // Rỗng = giờ làm tính thẳng cho việc (việc không có việc con)
        public Guid? SubTaskId { get; set; }

        public string? Title { get; set; }

        public double Minutes { get; set; }
    }

    // ---------------------------------------------------------------- Kiểm tra lịch của người nhận việc
    // Chỉ cho biết bận hay rảnh: không có tên dự án hay tiêu đề lịch cá nhân của người nhận
    public class GroupAssigneeAvailabilityDto
    {
        public Guid AssigneeId { get; set; }

        // Có giờ cố định trùng với lịch của người nhận
        public bool IsBusy { get; set; }

        public List<GroupBusyPeriodDto> Conflicts { get; set; } = new();

        // Khối lượng việc nhóm trong tuần của deadline; null khi việc chưa có deadline hoặc ước lượng
        public GroupWorkloadDto? Workload { get; set; }

        // IsBusy hoặc Workload.IsOverloaded: Leader cần xác nhận nếu vẫn giao
        public bool NeedsConfirmation { get; set; }
    }

    public class GroupBusyPeriodDto
    {
        public DateTimeOffset Start { get; set; }

        public DateTimeOffset End { get; set; }

        // PersonalSchedule (slot cá nhân), GoogleCalendar, hoặc GroupTask (việc nhóm khác có giờ cố định)
        public string Source { get; set; } = null!;
    }

    public class GroupWorkloadDto
    {
        // Tổng phút ước lượng của các việc nhóm chưa xong của người này có deadline trong 7 ngày kết thúc ở deadline, cộng việc đang xét
        public int AssignedMinutes { get; set; }

        // Số phút làm việc của một tuần theo cài đặt giờ làm của người nhận
        public int CapacityMinutes { get; set; }

        public bool IsOverloaded { get; set; }
    }

    // ---------------------------------------------------------------- Đề xuất đổi giờ
    public class GroupChangeRequestCreateDto
    {
        // Giờ cố định mới: gửi cả hai hoặc không gửi cái nào
        public DateTimeOffset? NewFixedStart { get; set; }

        public DateTimeOffset? NewFixedEnd { get; set; }

        public DateTimeOffset? NewDeadline { get; set; }

        public string Reason { get; set; } = null!;
    }

    public class GroupChangeDecisionDto
    {
        // Bắt buộc khi từ chối
        public string? Note { get; set; }

        // Khi duyệt: vẫn áp dụng dù giờ mới trùng lịch của người nhận (sau khi đã nhận cảnh báo 409)
        public bool ConfirmConflicts { get; set; }
    }

    public class GroupChangeRequestDto
    {
        public Guid Id { get; set; }

        public Guid GroupTaskId { get; set; }

        public string TaskTitle { get; set; } = null!;

        public Guid RequestedById { get; set; }

        public string RequestedByName { get; set; } = null!;

        public DateTimeOffset? CurrentFixedStart { get; set; }

        public DateTimeOffset? CurrentFixedEnd { get; set; }

        public DateTimeOffset? CurrentDeadline { get; set; }

        public DateTimeOffset? NewFixedStart { get; set; }

        public DateTimeOffset? NewFixedEnd { get; set; }

        public DateTimeOffset? NewDeadline { get; set; }

        public string Reason { get; set; } = null!;

        public string Status { get; set; } = null!;

        public Guid? DecidedById { get; set; }

        public string? DecidedByName { get; set; }

        public DateTimeOffset? DecidedAt { get; set; }

        public string? DecisionNote { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
