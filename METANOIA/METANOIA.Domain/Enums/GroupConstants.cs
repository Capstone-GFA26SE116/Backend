namespace METANOIA.Domain.Enums;

// Giá trị chuỗi của nhánh Group, khớp với các CHECK trong docs/sql/005_group_tables.sql.
// Dùng chuỗi (không dùng enum) để giống cách các bảng cũ lưu Status và Role.

public static class GroupRoles
{
    public const string Leader = "Leader";
    public const string Member = "Member";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [Leader, Member, Viewer];

    public static bool IsValid(string? role) => role is Leader or Member or Viewer;
}

public static class GroupStatuses
{
    public const string Active = "Active";
    public const string Archived = "Archived";
}

public static class GroupInvitationStatuses
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Declined = "Declined";
    public const string Expired = "Expired";
    public const string Revoked = "Revoked";
}

public static class GroupWorkStatuses
{
    // Việc và việc con
    public const string ToDo = "ToDo";
    public const string InProgress = "InProgress";
    public const string Done = "Done";
    public const string Skipped = "Skipped";

    // Giai đoạn
    public const string Planned = "Planned";
    public const string Delivered = "Delivered";
    public const string InRevision = "InRevision";
    public const string Approved = "Approved";

    public static bool IsTaskStatus(string? s) => s is ToDo or InProgress or Done or Skipped;

    public static bool IsMilestoneStatus(string? s) => s is Planned or InProgress or Delivered or InRevision or Approved;
}

public static class GroupPriorities
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";

    public static bool IsValid(string? p) => p is Low or Medium or High;
}

public static class GroupEntityTypes
{
    public const string Group = "Group";
    public const string Member = "Member";
    public const string Invitation = "Invitation";
    public const string Milestone = "Milestone";
    public const string Task = "Task";
    public const string SubTask = "SubTask";
    public const string Comment = "Comment";
    public const string FocusSession = "FocusSession";
    public const string ChangeRequest = "ChangeRequest";
}

public static class GroupChangeRequestStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";

    public static bool IsValid(string? s) => s is Pending or Approved or Rejected or Cancelled;
}

// Loại thông báo nhóm lưu trong cột Notification.Type (cần nới CHECK Notification_Type_check, xem 005_group_tables.sql bước 4)
public static class GroupNotificationTypes
{
    public const string Assigned = "GroupAssigned";
    public const string Mention = "GroupMention";
    public const string StatusChanged = "GroupStatusChanged";
    public const string Invited = "GroupInvited";

    // Cột Notification.Type tối đa 20 ký tự: hai tên dưới đây dài 20 và 18 ký tự nên vẫn vừa
    public const string ChangeRequested = "GroupChangeRequested";
    public const string ChangeDecided = "GroupChangeDecided";

    public static bool IsGroupType(string? type) =>
        type is Assigned or Mention or StatusChanged or Invited or ChangeRequested or ChangeDecided;
}
