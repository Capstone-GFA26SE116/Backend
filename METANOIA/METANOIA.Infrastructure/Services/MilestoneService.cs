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
        private const string InRevisionStatus = "InRevision";
        private const int MaxNameLength = 150;

        private static readonly HashSet<string> AllowedStatuses = new()
        {
            PlannedStatus, "InProgress", DeliveredStatus, InRevisionStatus, ApprovedStatus
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

            // Giai đoạn mới: các mốc giao và duyệt chỉ được ghi khi tạo thẳng ở trạng thái đó (nhập dữ liệu có sẵn)
            if (milestone.Status == DeliveredStatus) milestone.DeliveredAt = DateTime.UtcNow;
            if (milestone.Status == ApprovedStatus) milestone.ApprovedAt = DateTime.UtcNow;

            await _milestoneRepository.AddAsync(milestone, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(milestone);
        }

        public async Task<MilestoneDto> UpdateMilestoneAsync(Guid userId, Guid milestoneId, MilestoneRequestDto request, CancellationToken cancellationToken = default)
        {
            var milestone = await GetOwnedMilestoneAsync(userId, milestoneId, cancellationToken);
            ValidateRequest(request);
            await EnsureOrderIndexFreeAsync(milestone.ProjectId, request.OrderIndex, milestoneId, cancellationToken);

            // Không gửi status thì giữ nguyên
            var oldStatus = milestone.Status;
            var newStatus = string.IsNullOrWhiteSpace(request.Status) ? oldStatus : request.Status;
            StatusRules.EnsureMilestone(oldStatus, newStatus);

            // Chỉ giao được khi mọi task đã Done hoặc Skipped
            if (newStatus == DeliveredStatus && oldStatus != DeliveredStatus)
            {
                var open = await _milestoneRepository.CountOpenTasksAsync(milestoneId, cancellationToken);
                if (open > 0)
                {
                    throw new UserFriendlyException(
                        $"Còn {open} task chưa Done hoặc Skipped nên chưa giao được giai đoạn. Hãy hoàn thành hoặc bỏ qua các task đó trước.");
                }
            }

            milestone.Name = request.Name.Trim();
            milestone.OrderIndex = request.OrderIndex;
            milestone.DueDate = request.DueDate;

            // RevisionCount không bao giờ giảm qua PUT; mỗi lần vào InRevision tự cộng một
            milestone.RevisionCount = Math.Max(milestone.RevisionCount, request.RevisionCount);

            if (newStatus != oldStatus)
            {
                milestone.Status = newStatus;
                var now = DateTime.UtcNow;

                if (newStatus == DeliveredStatus) milestone.DeliveredAt = now;   // mỗi lần giao (kể cả giao lại sau chỉnh sửa)
                if (newStatus == ApprovedStatus) milestone.ApprovedAt = now;
                if (oldStatus == ApprovedStatus) milestone.ApprovedAt = null;    // mở lại sau khi đã duyệt
                if (newStatus == InRevisionStatus) milestone.RevisionCount += 1;
            }

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

        // Dùng khi tạo mới
        private static void ApplyRequest(Milestone milestone, MilestoneRequestDto request)
        {
            milestone.Name = request.Name.Trim();
            milestone.OrderIndex = request.OrderIndex;
            milestone.DueDate = request.DueDate;
            milestone.RevisionCount = request.RevisionCount;
            milestone.Status = string.IsNullOrWhiteSpace(request.Status) ? PlannedStatus : request.Status;
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
