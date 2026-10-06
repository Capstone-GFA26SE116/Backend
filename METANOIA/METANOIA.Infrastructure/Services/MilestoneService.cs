using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class MilestoneService : IMilestoneService
    {
        private const string PlannedStatus = "Planned";
        private const string DeliveredStatus = "Delivered";
        private const string ApprovedStatus = "Approved";
        private const int MaxNameLength = 150;

        private static readonly HashSet<string> AllowedStatuses = new()
        {
            PlannedStatus, "InProgress", DeliveredStatus, "InRevision", ApprovedStatus
        };

        private readonly IMilestoneRepository _milestoneRepository;
        private readonly IUnitOfWork _unitOfWork;

        public MilestoneService(IMilestoneRepository milestoneRepository, IUnitOfWork unitOfWork)
        {
            _milestoneRepository = milestoneRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<MilestoneDto>> GetMilestonesAsync(Guid userId, Guid projectId, CancellationToken cancellationToken = default)
        {
            await EnsureProjectOwnedAsync(userId, projectId, cancellationToken);

            var milestones = await _milestoneRepository.GetByProjectIdAsync(projectId, cancellationToken);
            return milestones.Select(ToDto).ToList();
        }

        public async Task<MilestoneDto> GetMilestoneAsync(Guid userId, Guid milestoneId, CancellationToken cancellationToken = default)
        {
            var milestone = await GetOwnedMilestoneAsync(userId, milestoneId, cancellationToken);
            return ToDto(milestone);
        }

        public async Task<MilestoneDto> CreateMilestoneAsync(Guid userId, Guid projectId, MilestoneRequestDto request, CancellationToken cancellationToken = default)
        {
            await EnsureProjectOwnedAsync(userId, projectId, cancellationToken);
            ValidateRequest(request);
            await EnsureOrderIndexFreeAsync(projectId, request.OrderIndex, Guid.Empty, cancellationToken);

            var milestone = new Milestone { Id = Guid.NewGuid(), ProjectId = projectId };
            ApplyRequest(milestone, request);

            await _milestoneRepository.AddAsync(milestone, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(milestone);
        }

        public async Task<MilestoneDto> UpdateMilestoneAsync(Guid userId, Guid milestoneId, MilestoneRequestDto request, CancellationToken cancellationToken = default)
        {
            var milestone = await GetOwnedMilestoneAsync(userId, milestoneId, cancellationToken);
            ValidateRequest(request);
            await EnsureOrderIndexFreeAsync(milestone.ProjectId, request.OrderIndex, milestoneId, cancellationToken);

            ApplyRequest(milestone, request);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(milestone);
        }

        public async Task DeleteMilestoneAsync(Guid userId, Guid milestoneId, CancellationToken cancellationToken = default)
        {
            var milestone = await GetOwnedMilestoneAsync(userId, milestoneId, cancellationToken);
            _milestoneRepository.Remove(milestone);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task EnsureProjectOwnedAsync(Guid userId, Guid projectId, CancellationToken cancellationToken)
        {
            if (!await _milestoneRepository.ProjectOwnedByUserAsync(projectId, userId, cancellationToken))
            {
                throw new NotFoundException("Không tìm thấy dự án.");
            }
        }

        private async Task<Milestone> GetOwnedMilestoneAsync(Guid userId, Guid milestoneId, CancellationToken cancellationToken)
        {
            return await _milestoneRepository.GetByIdForUserAsync(milestoneId, userId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy giai đoạn.");
        }

        private async Task EnsureOrderIndexFreeAsync(Guid projectId, int orderIndex, Guid excludeId, CancellationToken cancellationToken)
        {
            if (await _milestoneRepository.OrderIndexTakenAsync(projectId, orderIndex, excludeId, cancellationToken))
            {
                throw new UserFriendlyException("Số thứ tự giai đoạn đã tồn tại trong dự án này.");
            }
        }

        private static void ValidateRequest(MilestoneRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength)
            {
                throw new UserFriendlyException($"Tên giai đoạn không được để trống và tối đa {MaxNameLength} ký tự.");
            }

            if (request.OrderIndex < 1)
            {
                throw new UserFriendlyException("Số thứ tự giai đoạn phải từ 1 trở lên.");
            }

            if (request.RevisionCount < 0)
            {
                throw new UserFriendlyException("Số lần chỉnh sửa không được âm.");
            }

            var status = string.IsNullOrWhiteSpace(request.Status) ? PlannedStatus : request.Status;
            if (!AllowedStatuses.Contains(status))
            {
                throw new UserFriendlyException(
                    "Trạng thái không hợp lệ. Chọn một trong: Planned, InProgress, Delivered, InRevision, Approved.");
            }
        }

        private static void ApplyRequest(Milestone milestone, MilestoneRequestDto request)
        {
            milestone.Name = request.Name.Trim();
            milestone.OrderIndex = request.OrderIndex;
            milestone.DueDate = request.DueDate;
            milestone.RevisionCount = request.RevisionCount;
            milestone.Status = string.IsNullOrWhiteSpace(request.Status) ? PlannedStatus : request.Status;

            if (milestone.Status == DeliveredStatus && milestone.DeliveredAt is null)
            {
                milestone.DeliveredAt = DateTime.UtcNow;
            }

            if (milestone.Status == ApprovedStatus && milestone.ApprovedAt is null)
            {
                milestone.ApprovedAt = DateTime.UtcNow;
            }
        }

        private static MilestoneDto ToDto(Milestone milestone)
        {
            return new MilestoneDto
            {
                Id = milestone.Id,
                ProjectId = milestone.ProjectId,
                Name = milestone.Name,
                OrderIndex = milestone.OrderIndex,
                DueDate = milestone.DueDate,
                Status = milestone.Status,
                DeliveredAt = milestone.DeliveredAt,
                ApprovedAt = milestone.ApprovedAt,
                RevisionCount = milestone.RevisionCount
            };
        }
    }
}
