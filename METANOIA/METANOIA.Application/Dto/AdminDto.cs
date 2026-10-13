using System.Text.Json;

namespace METANOIA.Application.Dto
{
    public class PagedResultDto<T>
    {
        public List<T> Items { get; set; } = new();

        public int Page { get; set; }

        public int PageSize { get; set; }

        public int TotalCount { get; set; }
    }

    // ---------------------------------------------------------------- Users

    public class AdminUserListItemDto
    {
        public Guid Id { get; set; }

        public string Email { get; set; } = null!;

        public string FullName { get; set; } = null!;

        public string Role { get; set; } = null!;

        public bool OnboardingCompleted { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        // Có giá trị nghĩa là tài khoản đã bị xóa mềm
        public DateTimeOffset? DeletedAt { get; set; }

        // Gói đang Active (null nếu chưa có)
        public string? CurrentPlanName { get; set; }

        public DateTimeOffset? CurrentPlanEndAt { get; set; }
    }

    public class AdminUserDetailDto : AdminUserListItemDto
    {
        public string TimeZone { get; set; } = null!;

        public TimeOnly WorkDayStart { get; set; }

        public TimeOnly WorkDayEnd { get; set; }

        public bool WorksOnWeekend { get; set; }

        // Active | Expired | Revoked | NotConnected
        public string GoogleCalendarStatus { get; set; } = null!;

        public int ProjectCount { get; set; }

        public int TaskCount { get; set; }

        public int PaymentCount { get; set; }

        // Tổng tiền (VND) của các giao dịch Succeeded
        public long TotalPaid { get; set; }

        // Tổng lượt gọi AI qua mọi gói của user
        public int AiCallCount { get; set; }

        public List<AdminUserSubscriptionDto> Subscriptions { get; set; } = new();

        // 10 giao dịch gần nhất
        public List<AdminUserPaymentDto> RecentPayments { get; set; } = new();
    }

    public class AdminUserSubscriptionDto
    {
        public Guid Id { get; set; }

        public Guid PlanId { get; set; }

        public string PlanName { get; set; } = null!;

        public string Status { get; set; } = null!;

        public DateTimeOffset StartAt { get; set; }

        public DateTimeOffset? EndAt { get; set; }

        public int AiCallsUsed { get; set; }

        // null: không giới hạn
        public int? AiCallLimit { get; set; }
    }

    public class AdminUserPaymentDto
    {
        public Guid Id { get; set; }

        public long OrderCode { get; set; }

        public string PlanName { get; set; } = null!;

        public long Amount { get; set; }

        public string Status { get; set; } = null!;

        public DateTimeOffset CreatedAt { get; set; }
    }

    // ---------------------------------------------------------------- AI usage

    public class AiUsageReportDto
    {
        public DateTimeOffset From { get; set; }

        public DateTimeOffset To { get; set; }

        // feature | model | day
        public string GroupBy { get; set; } = null!;

        public AiUsageRowDto Total { get; set; } = new();

        public List<AiUsageRowDto> Rows { get; set; } = new();
    }

    public class AiUsageRowDto
    {
        // Tên feature, tên model, hoặc ngày (yyyy-MM-dd, giờ UTC). "Tổng" ở dòng tổng cộng
        public string Key { get; set; } = null!;

        public int Calls { get; set; }

        public int SucceededCalls { get; set; }

        public int FailedCalls { get; set; }

        // Số lượt đã trừ vào quota của user
        public int QuotaChargedCalls { get; set; }

        public long InputTokens { get; set; }

        public long OutputTokens { get; set; }

        public decimal Cost { get; set; }

        public double AverageLatencyMs { get; set; }
    }

    // ---------------------------------------------------------------- Focus tracks

    public class FocusTrackDto
    {
        public Guid Id { get; set; }

        public string Title { get; set; } = null!;

        public string Artist { get; set; } = null!;

        public string FileUrl { get; set; } = null!;

        public int DurationSeconds { get; set; }

        public bool IsActive { get; set; }
    }

    public class FocusTrackRequestDto
    {
        public string Title { get; set; } = null!;

        public string Artist { get; set; } = null!;

        public string FileUrl { get; set; } = null!;

        public int DurationSeconds { get; set; }

        public bool IsActive { get; set; } = true;
    }

    // ---------------------------------------------------------------- Audit logs

    public class AdminAuditLogDto
    {
        public Guid Id { get; set; }

        public Guid AdminId { get; set; }

        public string AdminEmail { get; set; } = null!;

        // Dạng "<TargetType>.<Create|Update|Delete>", ví dụ "SubscriptionPlan.Update"
        public string Action { get; set; } = null!;

        public string TargetType { get; set; } = null!;

        public Guid TargetId { get; set; }

        // Giá trị trước / sau (chỉ các trường đổi với Update). null khi Create (không có OldValue) hoặc Delete (không có NewValue)
        public JsonElement? OldValue { get; set; }

        public JsonElement? NewValue { get; set; }

        public string Reason { get; set; } = null!;

        public DateTimeOffset CreatedAt { get; set; }
    }
}
