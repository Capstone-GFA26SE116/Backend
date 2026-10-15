namespace METANOIA.Application.Dto
{
    // Dùng cho cả tạo (POST) và sửa (PUT) user bởi Admin. Khi sửa, trường nào bỏ trống (null) thì giữ nguyên.
    public class AdminUserRequestDto
    {
        // Bắt buộc khi tạo. Google đăng nhập bằng đúng email này sẽ nhận tài khoản
        public string? Email { get; set; }

        // Bắt buộc khi tạo
        public string? FullName { get; set; }

        // Tên role, ví dụ Freelancer hoặc Admin. Tạo mới mà bỏ trống thì là Freelancer
        public string? Role { get; set; }

        public string? TimeZone { get; set; }

        public TimeOnly? WorkDayStart { get; set; }

        public TimeOnly? WorkDayEnd { get; set; }

        public bool? WorksOnWeekend { get; set; }

        public int? DefaultBufferMinutes { get; set; }

        public int? ReminderLeadMinutes { get; set; }
    }
}
