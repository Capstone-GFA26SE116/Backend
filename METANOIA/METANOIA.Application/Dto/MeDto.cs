namespace METANOIA.Application.Dto
{
    public class MeDto
    {
        public Guid Id { get; set; }

        public string Email { get; set; } = null!;

        public string FullName { get; set; } = null!;

        public string Role { get; set; } = null!;

        public string TimeZone { get; set; } = null!;

        public TimeOnly WorkDayStart { get; set; }

        public TimeOnly WorkDayEnd { get; set; }

        public bool WorksOnWeekend { get; set; }

        public int DefaultBufferMinutes { get; set; }

        public int ReminderLeadMinutes { get; set; }

        public bool OnboardingCompleted { get; set; }

        // null nếu user chưa có gói Active nào
        public MeSubscriptionDto? CurrentSubscription { get; set; }

        // Active | Expired | Revoked | NotConnected
        public string GoogleCalendarStatus { get; set; } = null!;
    }

    public class MeSubscriptionDto
    {
        public Guid SubscriptionId { get; set; }

        public Guid PlanId { get; set; }

        public string PlanName { get; set; } = null!;

        // null: gói không hết hạn (gói Miễn phí)
        public DateTimeOffset? EndAt { get; set; }
    }

    // Hồ sơ người dùng tự sửa. Email gắn với tài khoản Google, role do Admin quản lý, giờ làm và nhắc việc thuộc về cài đặt (settings)
    public class UpdateProfileRequestDto
    {
        public string FullName { get; set; } = null!;
    }

    public class UpdateSettingsRequestDto
    {
        public TimeOnly WorkDayStart { get; set; }

        public TimeOnly WorkDayEnd { get; set; }

        public bool WorksOnWeekend { get; set; }

        public int DefaultBufferMinutes { get; set; }

        public int ReminderLeadMinutes { get; set; }

        public string TimeZone { get; set; } = null!;
    }

    public class OnboardingRequestDto
    {
        public TimeOnly WorkDayStart { get; set; }

        public TimeOnly WorkDayEnd { get; set; }

        public bool WorksOnWeekend { get; set; }

        // Các loại việc hay làm; phải là Domain đang IsActive
        public List<Guid> DomainIds { get; set; } = new();

        // Giờ làm hiệu quả, mỗi giá trị 0-23
        public List<int> ProductiveHours { get; set; } = new();

        // Rarely | Sometimes | Often | Always
        public string OverrunLevel { get; set; } = null!;
    }
}
