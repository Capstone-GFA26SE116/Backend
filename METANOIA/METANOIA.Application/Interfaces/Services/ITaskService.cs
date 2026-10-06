using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface ITaskService
    {
        Task<IReadOnlyList<TaskDto>> GetTasksAsync(Guid userId, Guid projectId, string? status, CancellationToken cancellationToken = default);

        Task<TaskDto> GetTaskAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default);

        Task<TaskDto> CreateTaskAsync(Guid userId, Guid projectId, TaskRequestDto request, CancellationToken cancellationToken = default);

        Task<TaskDto> UpdateTaskAsync(Guid userId, Guid taskId, TaskRequestDto request, CancellationToken cancellationToken = default);

        Task DeleteTaskAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default);
    }
}
