namespace METANOIA.Application.Dto
{
    // Quản trị nhóm (nhánh Group) cho Admin. Chỉ lộ thông tin cấp nhóm; KHÔNG có nội dung bình luận hay lịch cá nhân của thành viên.
    public class AdminGroupListItemDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        // Active hoặc Archived
        public string Status { get; set; } = null!;

        public Guid OwnerId { get; set; }

        public string OwnerName { get; set; } = null!;

        public string OwnerEmail { get; set; } = null!;

        public int MemberCount { get; set; }

        public int TaskCount { get; set; }

        public DateOnly? StartDate { get; set; }

        public DateOnly? Deadline { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public class AdminGroupDetailDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public string Status { get; set; } = null!;

        public Guid OwnerId { get; set; }

        public string OwnerName { get; set; } = null!;

        public string OwnerEmail { get; set; } = null!;

        public DateOnly? StartDate { get; set; }

        public DateOnly? Deadline { get; set; }

        public DateTime CreatedAt { get; set; }

        public List<GroupMemberDto> Members { get; set; } = new();

        // Cùng cấu trúc báo cáo tiến độ mà thành viên thấy
        public GroupProgressDto Progress { get; set; } = new();
    }

    public class AdminGroupStatsDto
    {
        public int TotalGroups { get; set; }

        public int ActiveGroups { get; set; }

        public int ArchivedGroups { get; set; }

        // Tổng số lượt tham gia (một người ở hai nhóm tính hai) và số người khác nhau đang ở trong ít nhất một nhóm
        public int TotalMemberships { get; set; }

        public int DistinctMembers { get; set; }

        public int TotalTasks { get; set; }

        // ToDo, InProgress, Done, Skipped (đủ cả bốn khóa, kể cả số 0)
        public Dictionary<string, int> TasksByStatus { get; set; } = new();

        // Nhóm tạo trong 30 ngày gần nhất
        public int GroupsCreatedLast30Days { get; set; }

        // Lời mời còn chờ và chưa quá hạn
        public int PendingInvitations { get; set; }

        // Phiên bấm giờ nhóm đang chạy
        public int RunningFocusSessions { get; set; }
    }
}
