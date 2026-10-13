using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IMeService
    {
        Task<MeDto> GetMeAsync(Guid userId, CancellationToken cancellationToken = default);

        // Xóa mềm (DeletedAt) và hủy liên kết Google Calendar. Token cũ bị từ chối ngay, không đăng nhập lại được.
        Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<MeDto> UpdateSettingsAsync(Guid userId, UpdateSettingsRequestDto request, CancellationToken cancellationToken = default);

        // Gọi lại nhiều lần được: ghi đè câu trả lời trước, không tạo trùng
        Task<MeDto> CompleteOnboardingAsync(Guid userId, OnboardingRequestDto request, CancellationToken cancellationToken = default);
    }
}
