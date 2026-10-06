using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IScheduleSlotService
    {
        Task<IReadOnlyList<ScheduleSlotDto>> GetSlotsAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default);

        Task<ScheduleSlotDto> GetSlotAsync(Guid userId, Guid slotId, CancellationToken cancellationToken = default);

        Task<ScheduleSlotDto> CreateSlotAsync(Guid userId, Guid taskId, ScheduleSlotRequestDto request, CancellationToken cancellationToken = default);

        Task<ScheduleSlotDto> UpdateSlotAsync(Guid userId, Guid slotId, ScheduleSlotRequestDto request, CancellationToken cancellationToken = default);

        // Không xóa thật: chuyển sang Cancelled để giữ lịch sử thay đổi
        Task<ScheduleSlotDto> CancelSlotAsync(Guid userId, Guid slotId, CancellationToken cancellationToken = default);
    }
}
