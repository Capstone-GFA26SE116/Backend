using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    // Hạn mức theo gói đang Active: số dự án và dung lượng file. Không có gói Active thì không chặn.
    // Vượt hạn mức chỉ chặn TẠO MỚI (không xóa gì của người dùng), ví dụ hết kỳ về gói Free khi đang có nhiều dự án.
    public interface IPlanLimitService
    {
        // Tạo dự án mới, hoặc khôi phục dự án đã lưu trữ
        Task EnsureCanCreateProjectAsync(Guid userId, CancellationToken cancellationToken = default);

        // Tải thêm một file dung lượng newBytes
        Task EnsureCanAddFileAsync(Guid userId, long newBytes, CancellationToken cancellationToken = default);

        Task<PlanUsageDto> GetUsageAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}
