namespace METANOIA.Application.Dto
{
    // Hạn mức của gói đang Active
    public record PlanLimits(string PlanName, int? ProjectLimit, int StorageLimitMb, DateTime StartAt, DateTime? EndAt);

    // Mức đã dùng so với hạn mức (GET /api/me/usage), để FE hiện "2/3 dự án, 40/100 MB" và tắt nút trước khi người dùng bấm
    public class PlanUsageDto
    {
        // null nếu người dùng không có gói Active (khi đó không áp hạn mức)
        public string? PlanName { get; set; }

        // Dự án đang hoạt động, không tính dự án lưu trữ và dự án "Cá nhân" mặc định
        public int ProjectsUsed { get; set; }

        // null: không giới hạn số dự án
        public int? ProjectLimit { get; set; }

        // Tổng dung lượng các file đã tải lên (link không tính)
        public long StorageUsedBytes { get; set; }

        // null: không áp hạn mức dung lượng (không có gói Active)
        public int? StorageLimitMb { get; set; }

        // Thời hạn gói đang dùng: bắt đầu từ ngày nào, hết ngày nào, còn mấy ngày. EndAt và RemainingDays là null với gói không hết hạn (Miễn phí)
        public DateTimeOffset? SubscriptionStartAt { get; set; }

        public DateTimeOffset? SubscriptionEndAt { get; set; }

        public int? RemainingDays { get; set; }
    }
}
