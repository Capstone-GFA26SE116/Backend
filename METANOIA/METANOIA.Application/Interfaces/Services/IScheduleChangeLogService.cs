using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IScheduleChangeLogService
    {
        Task<IReadOnlyList<ScheduleChangeLogDto>> GetChangesAsync(Guid userId, Guid slotId, CancellationToken cancellationToken = default);

        Task<ScheduleChangeLogDto> GetChangeAsync(Guid userId, Guid logId, CancellationToken cancellationToken = default);

        Task<ScheduleChangeLogDto> CreateChangeAsync(Guid userId, Guid slotId, ScheduleChangeLogRequestDto request, CancellationToken cancellationToken = default);
    }
}
