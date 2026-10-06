using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using DomainTask = METANOIA.Domain.Entities.Task;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class TaskService : ITaskService
    {
        private const string FlexibleType = "Flexible";
        private const string FixedType = "Fixed";
        private const string ToDoStatus = "ToDo";
        private const string DoneStatus = "Done";
        private const int MaxTitleLength = 200;

        private static readonly HashSet<string> AllowedStatuses = new()
        {
            ToDoStatus, "Scheduled", "InProgress", DoneStatus, "Skipped"
        };

        private readonly ITaskRepository _taskRepository;
        private readonly IUnitOfWork _unitOfWork;

        public TaskService(ITaskRepository taskRepository, IUnitOfWork unitOfWork)
        {
            _taskRepository = taskRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<TaskDto>> GetTasksAsync(Guid userId, Guid projectId, string? status, CancellationToken cancellationToken = default)
        {
            await EnsureProjectOwnedAsync(userId, projectId, cancellationToken);

            if (status is not null && !AllowedStatuses.Contains(status))
            {
                throw new UserFriendlyException("Trạng thái lọc không hợp lệ.");
            }

            var tasks = await _taskRepository.GetByProjectIdAsync(projectId, status, cancellationToken);
            return tasks.Select(ToDto).ToList();
        }

        public async Task<TaskDto> GetTaskAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default)
        {
            var task = await GetOwnedTaskAsync(userId, taskId, cancellationToken);
            return ToDto(task);
        }

        public async Task<TaskDto> CreateTaskAsync(Guid userId, Guid projectId, TaskRequestDto request, CancellationToken cancellationToken = default)
        {
            await EnsureProjectOwnedAsync(userId, projectId, cancellationToken);
            ValidateRequest(request);
            await ValidateReferencesAsync(projectId, request, cancellationToken);

            var task = new DomainTask
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                CreatedAt = DateTime.UtcNow
            };
            ApplyRequest(task, request);

            await _taskRepository.AddAsync(task, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(task);
        }

        public async Task<TaskDto> UpdateTaskAsync(Guid userId, Guid taskId, TaskRequestDto request, CancellationToken cancellationToken = default)
        {
            var task = await GetOwnedTaskAsync(userId, taskId, cancellationToken);
            ValidateRequest(request);
            await ValidateReferencesAsync(task.ProjectId, request, cancellationToken);

            ApplyRequest(task, request);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(task);
        }

        public async Task DeleteTaskAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default)
        {
            var task = await GetOwnedTaskAsync(userId, taskId, cancellationToken);

            if (await _taskRepository.HasHistoryAsync(taskId, cancellationToken))
            {
                throw new UserFriendlyException(
                    "Task đã có lịch sử tập trung hoặc đánh giá nên không thể xóa. Hãy đổi trạng thái sang Skipped thay thế.");
            }

            _taskRepository.Remove(task);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task EnsureProjectOwnedAsync(Guid userId, Guid projectId, CancellationToken cancellationToken)
        {
            if (!await _taskRepository.ProjectOwnedByUserAsync(projectId, userId, cancellationToken))
            {
                throw new NotFoundException("Không tìm thấy dự án.");
            }
        }

        private async Task<DomainTask> GetOwnedTaskAsync(Guid userId, Guid taskId, CancellationToken cancellationToken)
        {
            return await _taskRepository.GetByIdForUserAsync(taskId, userId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy task.");
        }

        private async Task ValidateReferencesAsync(Guid projectId, TaskRequestDto request, CancellationToken cancellationToken)
        {
            if (!await _taskRepository.DomainExistsAsync(request.DomainId, cancellationToken))
            {
                throw new UserFriendlyException("Domain không tồn tại.");
            }

            if (request.MilestoneId is { } milestoneId
                && !await _taskRepository.MilestoneBelongsToProjectAsync(milestoneId, projectId, cancellationToken))
            {
                throw new UserFriendlyException("Giai đoạn không tồn tại trong dự án này.");
            }
        }

        // Quy tắc CHECK theo loại: Flexible cần Deadline + EstimatedMinutes; Fixed cần FixedStart < FixedEnd
        private static void ValidateRequest(TaskRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > MaxTitleLength)
            {
                throw new UserFriendlyException($"Tiêu đề task không được để trống và tối đa {MaxTitleLength} ký tự.");
            }

            var status = string.IsNullOrWhiteSpace(request.Status) ? ToDoStatus : request.Status;
            if (!AllowedStatuses.Contains(status))
            {
                throw new UserFriendlyException("Trạng thái không hợp lệ. Chọn một trong: ToDo, Scheduled, InProgress, Done, Skipped.");
            }

            if (request.EstimatedMinutes is <= 0 || request.AiEstimatedMinutes is <= 0)
            {
                throw new UserFriendlyException("Số phút ước lượng phải lớn hơn 0.");
            }

            switch (request.TaskType)
            {
                case FlexibleType:
                    if (request.Deadline is null || request.EstimatedMinutes is null)
                    {
                        throw new UserFriendlyException("Task Flexible bắt buộc có Deadline và EstimatedMinutes.");
                    }
                    break;

                case FixedType:
                    if (request.FixedStart is null || request.FixedEnd is null)
                    {
                        throw new UserFriendlyException("Task Fixed bắt buộc có FixedStart và FixedEnd.");
                    }
                    if (request.FixedEnd <= request.FixedStart)
                    {
                        throw new UserFriendlyException("FixedEnd phải lớn hơn FixedStart.");
                    }
                    break;

                default:
                    throw new UserFriendlyException("TaskType phải là Flexible hoặc Fixed.");
            }
        }

        private static void ApplyRequest(DomainTask task, TaskRequestDto request)
        {
            var isFixed = request.TaskType == FixedType;

            task.Title = request.Title.Trim();
            task.Description = request.Description;
            task.TaskType = request.TaskType;
            task.Status = string.IsNullOrWhiteSpace(request.Status) ? ToDoStatus : request.Status;
            task.DomainId = request.DomainId;
            task.MilestoneId = request.MilestoneId;
            task.Deadline = request.Deadline?.UtcDateTime;
            task.EstimatedMinutes = request.EstimatedMinutes;
            task.AiEstimatedMinutes = request.AiEstimatedMinutes;
            task.FixedStart = isFixed ? request.FixedStart!.Value.UtcDateTime : null;
            task.FixedEnd = isFixed ? request.FixedEnd!.Value.UtcDateTime : null;

            if (task.Status == DoneStatus)
            {
                task.CompletedAt ??= DateTime.UtcNow;
            }
            else
            {
                task.CompletedAt = null;
            }
        }

        private static TaskDto ToDto(DomainTask task)
        {
            return new TaskDto
            {
                Id = task.Id,
                ProjectId = task.ProjectId,
                MilestoneId = task.MilestoneId,
                DomainId = task.DomainId,
                Title = task.Title,
                Description = task.Description,
                TaskType = task.TaskType,
                Status = task.Status,
                CreatedAt = AsUtc(task.CreatedAt),
                CompletedAt = AsUtc(task.CompletedAt),
                Deadline = AsUtc(task.Deadline),
                EstimatedMinutes = task.EstimatedMinutes,
                AiEstimatedMinutes = task.AiEstimatedMinutes,
                FixedStart = AsUtc(task.FixedStart),
                FixedEnd = AsUtc(task.FixedEnd)
            };
        }

        private static DateTimeOffset? AsUtc(DateTime? value)
        {
            return value is null ? null : AsUtc(value.Value);
        }

        private static DateTimeOffset AsUtc(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);
        }
    }
}
