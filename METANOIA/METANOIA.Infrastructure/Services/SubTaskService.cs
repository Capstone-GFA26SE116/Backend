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
        private readonly ITaskProgressService _progress;

        public SubTaskService(ISubTaskRepository subTaskRepository, IUnitOfWork unitOfWork, ITaskProgressService progress)
        {
            _subTaskRepository = subTaskRepository;
            _unitOfWork = unitOfWork;
            _progress = progress;
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
            ApplyRequest(subTask, request, string.IsNullOrWhiteSpace(request.Status) ? ToDoStatus : request.Status);

            // Subtask mới (ToDo) có thể làm Task đã Done phải mở lại: lưu và đồng bộ Task trong cùng một transaction
            var result = new ProgressResult { TaskId = taskId };
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await _subTaskRepository.AddAsync(subTask, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                result.Merge(await _progress.RefreshTaskAsync(taskId, cancellationToken));
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
            await _progress.AfterCommitAsync(result, cancellationToken);

            return ToDto(subTask);
        }

        public async Task<SubTaskDto> UpdateSubTaskAsync(Guid userId, Guid subTaskId, SubTaskRequestDto request, CancellationToken cancellationToken = default)
        {
            var subTask = await GetOwnedSubTaskAsync(userId, subTaskId, cancellationToken);
            ValidateRequest(request);
            await EnsureOrderIndexFreeAsync(subTask.TaskId, request.OrderIndex, subTaskId, cancellationToken);

            // Không gửi status thì giữ nguyên (trước đây tự về ToDo, làm mở lại subtask đã xong ngoài ý muốn)
            var newStatus = string.IsNullOrWhiteSpace(request.Status) ? subTask.Status : request.Status;
            StatusRules.EnsureSubTask(subTask.Status, newStatus);

            ApplyRequest(subTask, request, newStatus);

            var result = new ProgressResult { TaskId = subTask.TaskId };
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Subtask xong hoặc mở lại có thể làm Task tự Done hoặc mở lại
                result.Merge(await _progress.RefreshTaskAsync(subTask.TaskId, cancellationToken));
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
            await _progress.AfterCommitAsync(result, cancellationToken);

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

            var taskId = subTask.TaskId;
            var result = new ProgressResult { TaskId = taskId };
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                _subTaskRepository.Remove(subTask);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Xóa subtask cuối cùng còn dở có thể làm Task tự Done
                result.Merge(await _progress.RefreshTaskAsync(taskId, cancellationToken));
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
            await _progress.AfterCommitAsync(result, cancellationToken);
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

        private static void ApplyRequest(SubTask subTask, SubTaskRequestDto request, string status)
        {
            subTask.Title = request.Title.Trim();
            subTask.EstimatedMinutes = request.EstimatedMinutes;
            subTask.OrderIndex = request.OrderIndex;
            subTask.Status = status;
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
