using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class ScheduleChangeLogService : IScheduleChangeLogService
    {
        private static readonly HashSet<string> AllowedChangeTypes = new() { "Created", "Moved", "Resized", "Deleted" };
        private static readonly HashSet<string> AllowedSources = new() { "Manual", "AI", "GoogleSync" };

        private readonly IScheduleChangeLogRepository _logRepository;
        private readonly IScheduleSlotRepository _slotRepository;
        private readonly IUnitOfWork _unitOfWork;

        public ScheduleChangeLogService(
            IScheduleChangeLogRepository logRepository,
            IScheduleSlotRepository slotRepository,
            IUnitOfWork unitOfWork)
        {
            _logRepository = logRepository;
            _slotRepository = slotRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<ScheduleChangeLogDto>> GetChangesAsync(Guid userId, Guid slotId, CancellationToken cancellationToken = default)
        {
            await EnsureSlotOwnedAsync(userId, slotId, cancellationToken);

            var logs = await _logRepository.GetBySlotIdAsync(slotId, cancellationToken);
            return logs.Select(ToDto).ToList();
        }

        public async Task<ScheduleChangeLogDto> GetChangeAsync(Guid userId, Guid logId, CancellationToken cancellationToken = default)
        {
            var log = await _logRepository.GetByIdForUserAsync(logId, userId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy bản ghi thay đổi.");
            return ToDto(log);
        }

        // Ghi thủ công (ví dụ từ job AI hoặc đồng bộ Google); thay đổi trên slot tự động ghi log trong ScheduleSlotService
        public async Task<ScheduleChangeLogDto> CreateChangeAsync(Guid userId, Guid slotId, ScheduleChangeLogRequestDto request, CancellationToken cancellationToken = default)
        {
            await EnsureSlotOwnedAsync(userId, slotId, cancellationToken);

            if (!AllowedChangeTypes.Contains(request.ChangeType))
            {
                throw new UserFriendlyException("ChangeType phải là Created, Moved, Resized hoặc Deleted.");
            }

            if (!AllowedSources.Contains(request.Source))
            {
                throw new UserFriendlyException("Source phải là Manual, AI hoặc GoogleSync.");
            }

            var log = new ScheduleChangeLog
            {
                Id = Guid.NewGuid(),
                ScheduleSlotId = slotId,
                ChangeType = request.ChangeType,
                Source = request.Source,
                OldStart = request.OldStart?.UtcDateTime,
                OldEnd = request.OldEnd?.UtcDateTime,
                NewStart = request.NewStart?.UtcDateTime,
                NewEnd = request.NewEnd?.UtcDateTime,
                ChangedAt = DateTime.UtcNow
            };

            await _logRepository.AddAsync(log, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(log);
        }

        private async Task EnsureSlotOwnedAsync(Guid userId, Guid slotId, CancellationToken cancellationToken)
        {
            if (await _slotRepository.GetByIdForUserAsync(slotId, userId, cancellationToken) is null)
            {
                throw new NotFoundException("Không tìm thấy slot.");
            }
        }

        private static ScheduleChangeLogDto ToDto(ScheduleChangeLog log)
        {
            return new ScheduleChangeLogDto
            {
                Id = log.Id,
                ScheduleSlotId = log.ScheduleSlotId,
                ChangeType = log.ChangeType,
                Source = log.Source,
                OldStart = UtcTime.ToApi(log.OldStart),
                OldEnd = UtcTime.ToApi(log.OldEnd),
                NewStart = UtcTime.ToApi(log.NewStart),
                NewEnd = UtcTime.ToApi(log.NewEnd),
                ChangedAt = UtcTime.ToApi(log.ChangedAt)
            };
        }
    }
}
