using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class ProjectService : IProjectService
    {
        private const string ActiveStatus = "Active";
        private const string ArchivedStatus = "Archived";
        private const string DoneStatus = "Done";
        private const int MaxNameLength = 150;

        private readonly IProjectRepository _projectRepository;
        private readonly IUnitOfWork _unitOfWork;

        public ProjectService(IProjectRepository projectRepository, IUnitOfWork unitOfWork)
        {
            _projectRepository = projectRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<ProjectDto>> GetProjectsAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken = default)
        {
            var projects = await _projectRepository.GetByUserIdAsync(userId, includeArchived, cancellationToken);
            return projects.Select(ToDto).ToList();
        }

        public async Task<ProjectDto> GetProjectAsync(Guid userId, Guid projectId, CancellationToken cancellationToken = default)
        {
            var project = await GetOwnedProjectAsync(userId, projectId, cancellationToken);
            return ToDto(project);
        }

        public async Task<ProjectDto> CreateProjectAsync(Guid userId, ProjectRequestDto request, CancellationToken cancellationToken = default)
        {
            await ValidateRequestAsync(userId, request, cancellationToken);

            var project = new Project
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Status = ActiveStatus,
                IsDefault = false
            };
            ApplyRequest(project, request);

            await _projectRepository.AddAsync(project, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(project);
        }

        public async Task<ProjectDto> UpdateProjectAsync(Guid userId, Guid projectId, ProjectRequestDto request, CancellationToken cancellationToken = default)
        {
            await ValidateRequestAsync(userId, request, cancellationToken);

            var project = await GetOwnedProjectAsync(userId, projectId, cancellationToken);
            ApplyRequest(project, request);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(project);
        }

        public async Task<ProjectDto> ArchiveProjectAsync(Guid userId, Guid projectId, CancellationToken cancellationToken = default)
        {
            var project = await GetOwnedProjectAsync(userId, projectId, cancellationToken);
            project.Status = ArchivedStatus;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(project);
        }

        public async Task<ProjectDto> RestoreProjectAsync(Guid userId, Guid projectId, CancellationToken cancellationToken = default)
        {
            var project = await GetOwnedProjectAsync(userId, projectId, cancellationToken);
            project.Status = ActiveStatus;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(project);
        }

        public async Task<ProjectProgressDto> GetProgressAsync(Guid userId, Guid projectId, CancellationToken cancellationToken = default)
        {
            var project = await _projectRepository.GetWithProgressDataAsync(projectId, userId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy dự án.");

            var tasks = project.Tasks
                .Select(t => new ProjectTaskProgressDto
                {
                    TaskId = t.Id,
                    Title = t.Title,
                    Status = t.Status,
                    IsCompleted = t.CompletedAt.HasValue || t.Status == DoneStatus,
                    EstimatedMinutes = t.EstimatedMinutes ?? t.SubTasks.Sum(s => s.EstimatedMinutes),
                    ActualMinutes = (int)Math.Round(t.FocusSessions
                        .Where(f => f.EndedAt.HasValue)
                        .Sum(f => (f.EndedAt!.Value - f.StartedAt).TotalMinutes))
                })
                .OrderBy(t => t.Title)
                .ToList();

            var totalTasks = tasks.Count;
            var completedTasks = tasks.Count(t => t.IsCompleted);
            var estimatedMinutes = tasks.Sum(t => t.EstimatedMinutes);
            var actualMinutes = tasks.Sum(t => t.ActualMinutes);

            return new ProjectProgressDto
            {
                ProjectId = project.Id,
                Name = project.Name,
                Status = project.Status,
                Deadline = project.Deadline,
                TotalTasks = totalTasks,
                CompletedTasks = completedTasks,
                ProgressPercent = totalTasks == 0 ? 0 : Math.Round(completedTasks * 100.0 / totalTasks, 1),
                EstimatedMinutes = estimatedMinutes,
                ActualMinutes = actualMinutes,
                VarianceMinutes = actualMinutes - estimatedMinutes,
                Tasks = tasks
            };
        }

        private async Task<Project> GetOwnedProjectAsync(Guid userId, Guid projectId, CancellationToken cancellationToken)
        {
            return await _projectRepository.GetByIdForUserAsync(projectId, userId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy dự án.");
        }

        private async Task ValidateRequestAsync(Guid userId, ProjectRequestDto request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength)
            {
                throw new UserFriendlyException($"Tên dự án không được để trống và tối đa {MaxNameLength} ký tự.");
            }

            if (request.StartDate is { } start && request.Deadline is { } deadline && deadline < start)
            {
                throw new UserFriendlyException("Deadline không được trước ngày bắt đầu.");
            }

            if (request.ClientId is { } clientId
                && !await _projectRepository.ClientBelongsToUserAsync(clientId, userId, cancellationToken))
            {
                throw new UserFriendlyException("Khách hàng không tồn tại.");
            }
        }

        private static void ApplyRequest(Project project, ProjectRequestDto request)
        {
            project.Name = request.Name.Trim();
            project.Description = request.Description;
            project.StartDate = request.StartDate;
            project.Deadline = request.Deadline;
            project.ClientId = request.ClientId;
        }

        private static ProjectDto ToDto(Project project)
        {
            return new ProjectDto
            {
                Id = project.Id,
                Name = project.Name,
                Description = project.Description,
                StartDate = project.StartDate,
                Deadline = project.Deadline,
                Status = project.Status,
                ClientId = project.ClientId,
                CreatedAt = project.CreatedAt
            };
        }
    }
}
