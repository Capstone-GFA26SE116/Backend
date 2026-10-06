using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class SubTaskService : ISubTaskService
    {
        private const string ToDoStatus = "ToDo";
        private const int MaxTitleLength = 200;

        private static readonly HashSet<string> AllowedStatuses = new() { ToDoStatus, "Done", "Skipped" };

        private readonly ISubTaskRepository _subTaskRepository;
        private readonly IUnitOfWork _unitOfWork;

        public SubTaskService(ISubTaskRepository subTaskRepository, IUnitOfWork unitOfWork)
        {
            _subTaskRepository = subTaskRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<SubTaskDto>> GetSubTasksAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default)
        {
            await EnsureTaskOwnedAsync(userId, taskId, cancellationToken);

            var subTasks = await _subTaskRepository.GetByTaskIdAsync(taskId, cancellationToken);
            return subTasks.Select(ToDto).ToList();
        }

        public async Task<SubTaskDto> GetSubTaskAsync(Guid userId, Guid subTaskId, CancellationToken cancellationToken = default)
        {
            var subTask = await GetOwnedSubTaskAsync(userId, subTaskId, cancellationToken);
            return ToDto(subTask);
        }

        public async Task<SubTaskDto> CreateSubTaskAsync(Guid userId, Guid taskId, SubTaskRequestDto request, CancellationToken cancellationToken = default)
        {
            await EnsureTaskOwnedAsync(userId, taskId, cancellationToken);
            ValidateRequest(request);
            await EnsureOrderIndexFreeAsync(taskId, request.OrderIndex, Guid.Empty, cancellationToken);

            var subTask = new SubTask { Id = Guid.NewGuid(), TaskId = taskId };
            ApplyRequest(subTask, request);

            await _subTaskRepository.AddAsync(subTask, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(subTask);
        }

        public async Task<SubTaskDto> UpdateSubTaskAsync(Guid userId, Guid subTaskId, SubTaskRequestDto request, CancellationToken cancellationToken = default)
        {
            var subTask = await GetOwnedSubTaskAsync(userId, subTaskId, cancellationToken);
            ValidateRequest(request);
            await EnsureOrderIndexFreeAsync(subTask.TaskId, request.OrderIndex, subTaskId, cancellationToken);

            ApplyRequest(subTask, request);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(subTask);
        }

        public async Task DeleteSubTaskAsync(Guid userId, Guid subTaskId, CancellationToken cancellationToken = default)
        {
            var subTask = await GetOwnedSubTaskAsync(userId, subTaskId, cancellationToken);

            if (await _subTaskRepository.HasHistoryAsync(subTaskId, cancellationToken))
            {
                throw new UserFriendlyException(
                    "Subtask đã có lịch sử tập trung hoặc đánh giá nên không thể xóa. Hãy đổi trạng thái sang Skipped thay thế.");
            }

            _subTaskRepository.Remove(subTask);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task EnsureTaskOwnedAsync(Guid userId, Guid taskId, CancellationToken cancellationToken)
        {
            if (!await _subTaskRepository.TaskOwnedByUserAsync(taskId, userId, cancellationToken))
            {
                throw new NotFoundException("Không tìm thấy task.");
            }
        }

        private async Task<SubTask> GetOwnedSubTaskAsync(Guid userId, Guid subTaskId, CancellationToken cancellationToken)
        {
            return await _subTaskRepository.GetByIdForUserAsync(subTaskId, userId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy subtask.");
        }

        private async Task EnsureOrderIndexFreeAsync(Guid taskId, int orderIndex, Guid excludeId, CancellationToken cancellationToken)
        {
            if (await _subTaskRepository.OrderIndexTakenAsync(taskId, orderIndex, excludeId, cancellationToken))
            {
                throw new UserFriendlyException("Số thứ tự subtask đã tồn tại trong task này.");
            }
        }

        private static void ValidateRequest(SubTaskRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > MaxTitleLength)
            {
                throw new UserFriendlyException($"Tên subtask không được để trống và tối đa {MaxTitleLength} ký tự.");
            }

            if (request.EstimatedMinutes <= 0)
            {
                throw new UserFriendlyException("Số phút ước lượng phải lớn hơn 0.");
            }

            if (request.OrderIndex < 1)
            {
                throw new UserFriendlyException("Số thứ tự subtask phải từ 1 trở lên.");
            }

            if (!AllowedStatuses.Contains(string.IsNullOrWhiteSpace(request.Status) ? ToDoStatus : request.Status))
            {
                throw new UserFriendlyException("Trạng thái không hợp lệ. Chọn một trong: ToDo, Done, Skipped.");
            }
        }

        private static void ApplyRequest(SubTask subTask, SubTaskRequestDto request)
        {
            subTask.Title = request.Title.Trim();
            subTask.EstimatedMinutes = request.EstimatedMinutes;
            subTask.OrderIndex = request.OrderIndex;
            subTask.Status = string.IsNullOrWhiteSpace(request.Status) ? ToDoStatus : request.Status;
        }

        private static SubTaskDto ToDto(SubTask subTask)
        {
            return new SubTaskDto
            {
                Id = subTask.Id,
                TaskId = subTask.TaskId,
                Title = subTask.Title,
                EstimatedMinutes = subTask.EstimatedMinutes,
                OrderIndex = subTask.OrderIndex,
                Status = subTask.Status
            };
        }
    }
}
