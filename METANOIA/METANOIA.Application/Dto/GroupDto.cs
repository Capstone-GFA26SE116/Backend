namespace METANOIA.Application.Dto
{
    // ---------------------------------------------------------------- Nhóm
    public class GroupRequestDto
    {
        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public DateOnly? StartDate { get; set; }

        public DateOnly? Deadline { get; set; }
    }

    public class GroupDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public Guid OwnerId { get; set; }

        public DateOnly? StartDate { get; set; }

        public DateOnly? Deadline { get; set; }

        public string Status { get; set; } = null!;

        public DateTime CreatedAt { get; set; }

        // Vai trò của người đang gọi trong nhóm này
        public string MyRole { get; set; } = null!;

        public int MemberCount { get; set; }
    }

    // ---------------------------------------------------------------- Thành viên và lời mời
    public class GroupMemberDto
    {
        public Guid UserId { get; set; }

        public string FullName { get; set; } = null!;

        public string Email { get; set; } = null!;

        public string Role { get; set; } = null!;

        public DateTime JoinedAt { get; set; }
    }

    public class GroupMemberRoleRequestDto
    {
        public string Role { get; set; } = null!;
    }

    public class GroupInvitationRequestDto
    {
        public string Email { get; set; } = null!;

        // Bỏ trống thì là Member
        public string? Role { get; set; }
    }

    public class GroupInvitationDto
    {
        public Guid Id { get; set; }

        public Guid GroupId { get; set; }

        public string GroupName { get; set; } = null!;

        public string Email { get; set; } = null!;

        public string Role { get; set; } = null!;

        public string Status { get; set; } = null!;

        public Guid InvitedById { get; set; }

        public string InvitedByName { get; set; } = null!;

        public DateTime CreatedAt { get; set; }

        public DateTime ExpiresAt { get; set; }

        public DateTime? RespondedAt { get; set; }
    }

    // Trả ra đúng một lần khi tạo lời mời (server chỉ giữ bản băm); Leader gửi token này cho người được mời nếu muốn dùng liên kết
    public class GroupInvitationCreatedDto : GroupInvitationDto
    {
        public string AcceptToken { get; set; } = null!;
    }

    public class GroupInvitationTokenRequestDto
    {
        public string Token { get; set; } = null!;
    }

    // ---------------------------------------------------------------- Giai đoạn
    public class GroupMilestoneRequestDto
    {
        public string Name { get; set; } = null!;

        public int OrderIndex { get; set; }

        public DateOnly? DueDate { get; set; }

        // Bỏ trống thì giữ nguyên (khi sửa) hoặc Planned (khi tạo)
        public string? Status { get; set; }

        // Vẫn chuyển sang Delivered dù còn việc chưa xong (sau khi đã nhận cảnh báo 409)
        public bool ConfirmOpenTasks { get; set; }
    }

    public class GroupMilestoneDto
    {
        public Guid Id { get; set; }

        public Guid GroupId { get; set; }

        public string Name { get; set; } = null!;

        public int OrderIndex { get; set; }

        public DateOnly? DueDate { get; set; }

        public string Status { get; set; } = null!;

        public DateTime? DeliveredAt { get; set; }

        public int RevisionCount { get; set; }
    }

    public class GroupOpenTaskDto
    {
        public Guid Id { get; set; }

        public string Title { get; set; } = null!;

        public string Status { get; set; } = null!;

        public int OpenSubTaskCount { get; set; }
    }

    // ---------------------------------------------------------------- Việc
    public class GroupTaskRequestDto
    {
        public string Title { get; set; } = null!;

        public string? Description { get; set; }

        // Bỏ trống thì là Medium
        public string? Priority { get; set; }

        public Guid? MilestoneId { get; set; }

        // Chỉ dùng khi tạo. Khi sửa, đổi người được giao qua PUT /api/group-tasks/{id}/assignee
        public Guid? AssigneeId { get; set; }

        public DateTimeOffset? Deadline { get; set; }

        public int? EstimatedMinutes { get; set; }

        // Giờ cố định (họp, hẹn): gửi cả hai hoặc không gửi cái nào. Có giờ cố định thì khi giao việc sẽ kiểm tra trùng lịch người nhận
        public DateTimeOffset? FixedStart { get; set; }

        public DateTimeOffset? FixedEnd { get; set; }

        // Vẫn giao dù người nhận bận hoặc quá tải (sau khi đã nhận cảnh báo 409 GROUP_ASSIGNEE_BUSY)
        public bool ConfirmConflicts { get; set; }
    }

    public class GroupTaskStatusRequestDto
    {
        public string Status { get; set; } = null!;
    }

    public class GroupAssigneeRequestDto
    {
        // Rỗng = bỏ giao việc
        public Guid? AssigneeId { get; set; }

        // Vẫn giao dù người nhận bận hoặc quá tải (chỉ áp dụng cho việc, không áp dụng cho việc con)
        public bool ConfirmConflicts { get; set; }
    }

    public class GroupTaskDto
    {
        public Guid Id { get; set; }

        public Guid GroupId { get; set; }

        public Guid? MilestoneId { get; set; }

        public Guid? AssigneeId { get; set; }

        public string? AssigneeName { get; set; }

        public string Title { get; set; } = null!;

        public string? Description { get; set; }

        public string Priority { get; set; } = null!;

        public string Status { get; set; } = null!;

        public DateTimeOffset? Deadline { get; set; }

        public DateTimeOffset? FixedStart { get; set; }

        public DateTimeOffset? FixedEnd { get; set; }

        public int? EstimatedMinutes { get; set; }

        public int SubTaskCount { get; set; }

        public int SubTaskDoneCount { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? CompletedAt { get; set; }
    }

    // ---------------------------------------------------------------- Việc con
    public class GroupSubTaskRequestDto
    {
        public string Title { get; set; } = null!;

        public int? EstimatedMinutes { get; set; }

        // Bỏ trống thì xếp cuối danh sách
        public int? OrderIndex { get; set; }

        // Chỉ dùng khi tạo. Khi sửa, đổi người được giao qua PUT /api/group-subtasks/{id}/assignee
        public Guid? AssigneeId { get; set; }
    }

    public class GroupSubTaskDto
    {
        public Guid Id { get; set; }

        public Guid GroupTaskId { get; set; }

        public Guid? AssigneeId { get; set; }

        public string? AssigneeName { get; set; }

        public string Title { get; set; } = null!;

        public int? EstimatedMinutes { get; set; }

        public int OrderIndex { get; set; }

        public string Status { get; set; } = null!;

        public Guid? CompletedById { get; set; }

        public DateTime? CompletedAt { get; set; }
    }

    // ---------------------------------------------------------------- Bình luận, nhật ký
    public class GroupCommentRequestDto
    {
        public string Content { get; set; } = null!;
    }

    public class GroupCommentDto
    {
        public Guid Id { get; set; }

        public Guid GroupTaskId { get; set; }

        public Guid AuthorId { get; set; }

        public string AuthorName { get; set; } = null!;

        public string Content { get; set; } = null!;

        public DateTime CreatedAt { get; set; }

        public DateTime? EditedAt { get; set; }
    }

    public class GroupActivityDto
    {
        public Guid Id { get; set; }

        public Guid ActorId { get; set; }

        public string ActorName { get; set; } = null!;

        public string EntityType { get; set; } = null!;

        public Guid? EntityId { get; set; }

        public string Action { get; set; } = null!;

        public string? OldValue { get; set; }

        public string? NewValue { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    // ---------------------------------------------------------------- Tiến độ và việc của tôi
    public class GroupProgressDto
    {
        public Guid GroupId { get; set; }

        public int TotalTasks { get; set; }

        // Tổng giờ làm thực tế của cả nhóm (phút, các phiên đã kết thúc) và tổng ước lượng của các việc có ước lượng
        public double TotalTrackedMinutes { get; set; }

        public int TotalEstimatedMinutes { get; set; }

        // ToDo, InProgress, Done, Skipped (đủ cả bốn khóa, kể cả số 0)
        public Dictionary<string, int> TasksByStatus { get; set; } = new();

        // Việc còn lại = chưa Done và chưa Skipped
        public int RemainingTasks { get; set; }

        public int UnassignedTasks { get; set; }

        // Done trên (tổng trừ Skipped), làm tròn 1 chữ số thập phân; 0 khi chưa có việc
        public double CompletedPercent { get; set; }

        public List<GroupMemberProgressDto> Members { get; set; } = new();

        public List<GroupMilestoneProgressDto> Milestones { get; set; } = new();

        // Tín hiệu giúp Leader theo dõi: quá hạn, chưa có lịch, nguy cơ trễ, đứng yên (tính lúc xem, không lưu)
        public GroupSignalsDto Signals { get; set; } = new();
    }

    // Chỉ tính cho việc đang mở (ToDo hoặc InProgress) và đã có người được giao (việc, hoặc một việc con giao riêng). Không lộ giờ hay
    // tiêu đề lịch cá nhân của ai, chỉ có số phút và cờ
    public class GroupSignalsDto
    {
        public int OverdueTasks { get; set; }

        public int UnscheduledTasks { get; set; }

        public int AtRiskTasks { get; set; }

        public int StaleTasks { get; set; }

        // Các việc có ít nhất một tín hiệu
        public List<GroupTaskSignalDto> Tasks { get; set; } = new();
    }

    public class GroupTaskSignalDto
    {
        public Guid TaskId { get; set; }

        public string Title { get; set; } = null!;

        public Guid? AssigneeId { get; set; }

        public string? AssigneeName { get; set; }

        public string Status { get; set; } = null!;

        public DateTimeOffset? Deadline { get; set; }

        // Deadline đã qua mà việc chưa xong
        public bool IsOverdue { get; set; }

        // Có ít nhất một slot đang sống (Approved, Due, InProgress, Overrun) của việc hoặc các việc con
        public bool HasSchedule { get; set; }

        // Chưa quá hạn, deadline trong vài ngày tới và số phút còn phải làm lớn hơn tổng phút đã xếp (slot chưa diễn ra, kết thúc trước deadline)
        public bool AtRisk { get; set; }

        // Lần hoạt động cuối (nhật ký, bình luận, phiên bấm giờ, hoặc ngày tạo) đã quá vài ngày
        public bool IsStale { get; set; }

        // Tổng phút slot chưa diễn ra, kết thúc trước deadline (cùng cách tính với AtRisk)
        public int ScheduledMinutes { get; set; }

        public int? EstimatedMinutes { get; set; }

        public DateTimeOffset LastActivityAt { get; set; }
    }

    public class GroupMemberProgressDto
    {
        public Guid UserId { get; set; }

        public string FullName { get; set; } = null!;

        public string Role { get; set; } = null!;

        public int AssignedTasks { get; set; }

        public int DoneTasks { get; set; }

        public int AssignedSubTasks { get; set; }

        public int DoneSubTasks { get; set; }

        // Giờ làm thực tế của người này trong nhóm (phút, các phiên đã kết thúc)
        public double TrackedMinutes { get; set; }

        // Số việc đang mở của người này có từng tín hiệu (xem GroupSignalsDto)
        public int OverdueTasks { get; set; }

        public int UnscheduledTasks { get; set; }

        public int AtRiskTasks { get; set; }

        public int StaleTasks { get; set; }
    }

    public class GroupMilestoneProgressDto
    {
        public Guid MilestoneId { get; set; }

        public string Name { get; set; } = null!;

        public string Status { get; set; } = null!;

        public DateOnly? DueDate { get; set; }

        public int TotalTasks { get; set; }

        public int DoneTasks { get; set; }
    }

    public class MyGroupWorkDto
    {
        public List<MyGroupTaskDto> Tasks { get; set; } = new();

        public List<MyGroupSubTaskDto> SubTasks { get; set; } = new();
    }

    public class MyGroupTaskDto
    {
        public Guid TaskId { get; set; }

        public Guid GroupId { get; set; }

        public string GroupName { get; set; } = null!;

        public string Title { get; set; } = null!;

        public string Status { get; set; } = null!;

        public string Priority { get; set; } = null!;

        public DateTimeOffset? Deadline { get; set; }

        public DateTimeOffset? FixedStart { get; set; }

        public DateTimeOffset? FixedEnd { get; set; }
    }

    public class MyGroupSubTaskDto
    {
        public Guid SubTaskId { get; set; }

        public Guid TaskId { get; set; }

        public Guid GroupId { get; set; }

        public string GroupName { get; set; } = null!;

        public string TaskTitle { get; set; } = null!;

        public string Title { get; set; } = null!;

        public string Status { get; set; } = null!;
    }
}
