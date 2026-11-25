using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

// Nhánh Group (làm việc nhóm). Bảng riêng, không đụng nhánh cá nhân. Xem docs/GROUP_FEATURE.md.
// Bảng chính tên "WorkGroup" vì GROUP là từ khóa SQL; các cột tham chiếu vẫn gọi GroupId.

public partial class WorkGroup
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public Guid OwnerId { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? Deadline { get; set; }

    public string Status { get; set; } = "Active";

    public DateTime CreatedAt { get; set; }

    public virtual User Owner { get; set; } = null!;

    public virtual ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();

    public virtual ICollection<GroupInvitation> Invitations { get; set; } = new List<GroupInvitation>();

    public virtual ICollection<GroupMilestone> Milestones { get; set; } = new List<GroupMilestone>();

    public virtual ICollection<GroupTask> Tasks { get; set; } = new List<GroupTask>();

    public virtual ICollection<GroupActivityLog> ActivityLogs { get; set; } = new List<GroupActivityLog>();

    // Chat của nhóm (ChatConversation.Scope = Group, docs/sql/008_chat_group.sql); chưa có API
    public virtual ICollection<ChatConversation> ChatConversations { get; set; } = new List<ChatConversation>();
}

public partial class GroupMember
{
    public Guid Id { get; set; }

    public Guid GroupId { get; set; }

    public Guid UserId { get; set; }

    public string Role { get; set; } = null!;

    public DateTime JoinedAt { get; set; }

    public virtual WorkGroup Group { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}

public partial class GroupInvitation
{
    public Guid Id { get; set; }

    public Guid GroupId { get; set; }

    public string Email { get; set; } = null!;

    public string Role { get; set; } = null!;

    // Chỉ lưu bản băm SHA-256 của token, giống RefreshToken
    public string TokenHash { get; set; } = null!;

    public Guid InvitedById { get; set; }

    public string Status { get; set; } = "Pending";

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    public virtual WorkGroup Group { get; set; } = null!;

    public virtual User InvitedBy { get; set; } = null!;
}

public partial class GroupMilestone
{
    public Guid Id { get; set; }

    public Guid GroupId { get; set; }

    public string Name { get; set; } = null!;

    public int OrderIndex { get; set; }

    public DateOnly? DueDate { get; set; }

    public string Status { get; set; } = "Planned";

    public DateTime? DeliveredAt { get; set; }

    public int RevisionCount { get; set; }

    public virtual WorkGroup Group { get; set; } = null!;

    public virtual ICollection<GroupTask> Tasks { get; set; } = new List<GroupTask>();
}

public partial class GroupTask
{
    public Guid Id { get; set; }

    public Guid GroupId { get; set; }

    public Guid? MilestoneId { get; set; }

    public Guid? AssigneeId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string Priority { get; set; } = "Medium";

    public string Status { get; set; } = "ToDo";

    public DateTime? Deadline { get; set; }

    // Việc có giờ cố định (họp, hẹn): có cả hai hoặc không có cả hai. Dùng để kiểm tra trùng lịch khi giao việc
    public DateTime? FixedStart { get; set; }

    public DateTime? FixedEnd { get; set; }

    public int? EstimatedMinutes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public virtual WorkGroup Group { get; set; } = null!;

    public virtual GroupMilestone? Milestone { get; set; }

    public virtual User? Assignee { get; set; }

    public virtual ICollection<GroupSubTask> SubTasks { get; set; } = new List<GroupSubTask>();

    public virtual ICollection<GroupTaskComment> Comments { get; set; } = new List<GroupTaskComment>();

    public virtual ICollection<GroupFocusSession> FocusSessions { get; set; } = new List<GroupFocusSession>();

    public virtual ICollection<GroupTaskChangeRequest> ChangeRequests { get; set; } = new List<GroupTaskChangeRequest>();
}

public partial class GroupSubTask
{
    public Guid Id { get; set; }

    public Guid GroupTaskId { get; set; }

    public Guid? AssigneeId { get; set; }

    public string Title { get; set; } = null!;

    public int? EstimatedMinutes { get; set; }

    public int OrderIndex { get; set; }

    public string Status { get; set; } = "ToDo";

    public Guid? CompletedById { get; set; }

    public DateTime? CompletedAt { get; set; }

    public virtual GroupTask GroupTask { get; set; } = null!;

    public virtual User? Assignee { get; set; }

    public virtual User? CompletedBy { get; set; }
}

public partial class GroupTaskComment
{
    public Guid Id { get; set; }

    public Guid GroupTaskId { get; set; }

    public Guid AuthorId { get; set; }

    public string Content { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? EditedAt { get; set; }

    public virtual GroupTask GroupTask { get; set; } = null!;

    public virtual User Author { get; set; } = null!;
}

public partial class GroupActivityLog
{
    public Guid Id { get; set; }

    public Guid GroupId { get; set; }

    public Guid ActorId { get; set; }

    public string EntityType { get; set; } = null!;

    public Guid? EntityId { get; set; }

    public string Action { get; set; } = null!;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual WorkGroup Group { get; set; } = null!;

    public virtual User Actor { get; set; } = null!;
}

// Một lần làm của MỘT người trên một việc (hoặc một việc con). Giờ làm của từng người là tổng các phiên đã kết thúc.
public partial class GroupFocusSession
{
    public Guid Id { get; set; }

    public Guid GroupTaskId { get; set; }

    public Guid? GroupSubTaskId { get; set; }

    public Guid UserId { get; set; }

    public DateTime StartedAt { get; set; }

    // Rỗng = đang chạy
    public DateTime? EndedAt { get; set; }

    public string? StopReason { get; set; }

    public string? Note { get; set; }

    public string CaptureMethod { get; set; } = "Timer";

    // Slot đã xếp mà phiên này làm theo (tùy chọn); xóa slot thì để rỗng
    public Guid? ScheduleSlotId { get; set; }

    public virtual GroupTask GroupTask { get; set; } = null!;

    public virtual GroupSubTask? GroupSubTask { get; set; }

    public virtual ScheduleSlot? ScheduleSlot { get; set; }

    public virtual User User { get; set; } = null!;
}

// Thành viên đề xuất đổi giờ cố định hoặc deadline của việc; Leader duyệt hoặc từ chối
public partial class GroupTaskChangeRequest
{
    public Guid Id { get; set; }

    public Guid GroupTaskId { get; set; }

    public Guid RequestedById { get; set; }

    public DateTime? NewFixedStart { get; set; }

    public DateTime? NewFixedEnd { get; set; }

    public DateTime? NewDeadline { get; set; }

    public string Reason { get; set; } = null!;

    public string Status { get; set; } = "Pending";

    public Guid? DecidedById { get; set; }

    public DateTime? DecidedAt { get; set; }

    public string? DecisionNote { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual GroupTask GroupTask { get; set; } = null!;

    public virtual User RequestedBy { get; set; } = null!;

    public virtual User? DecidedBy { get; set; }
}
