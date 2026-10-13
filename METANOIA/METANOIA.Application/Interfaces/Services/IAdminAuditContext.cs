namespace METANOIA.Application.Interfaces.Services
{
    // Cho tầng dữ liệu biết thao tác ghi hiện tại có phải của Admin cần ghi AdminActionLog hay không.
    // Cài đặt nằm ở web project (đọc HttpContext) nên Infrastructure không phụ thuộc ASP.NET.
    public interface IAdminAuditContext
    {
        // true: request đang chạy là thao tác ghi của một endpoint chỉ dành cho Admin
        bool IsActive { get; }

        Guid? AdminId { get; }

        // Lý do Admin nhập (header X-Admin-Reason); mặc định "Không ghi lý do"
        string Reason { get; }
    }
}
