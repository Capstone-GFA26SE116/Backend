using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface ISubTaskService
    {
        Task<IReadOnlyList<SubTaskDto>> GetSubTasksAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default);

        Task<SubTaskDto> GetSubTaskAsync(Guid userId, Guid subTaskId, CancellationToken cancellationToken = default);

        Task<SubTaskDto> CreateSubTaskAsync(Guid userId, Guid taskId, SubTaskRequestDto request, CancellationToken cancellationToken = default);

        Task<SubTaskDto> UpdateSubTaskAsync(Guid userId, Guid subTaskId, SubTaskRequestDto request, CancellationToken cancellationToken = default);

        Task DeleteSubTaskAsync(Guid userId, Guid subTaskId, CancellationToken cancellationToken = default);
    }
}
