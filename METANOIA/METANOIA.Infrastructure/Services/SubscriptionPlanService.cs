using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class SubscriptionPlanService : ISubscriptionPlanService
    {
        private const int MaxNameLength = 50;

        private readonly ISubscriptionPlanRepository _planRepository;
        private readonly IUnitOfWork _unitOfWork;

        public SubscriptionPlanService(ISubscriptionPlanRepository planRepository, IUnitOfWork unitOfWork)
        {
            _planRepository = planRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default)
        {
            var plans = await _planRepository.GetAllAsync(cancellationToken);
            return plans.Select(ToDto).ToList();
        }

        public async Task<SubscriptionPlanDto> GetPlanAsync(Guid planId, CancellationToken cancellationToken = default)
        {
            var plan = await GetExistingAsync(planId, cancellationToken);
            return ToDto(plan);
        }

        public async Task<SubscriptionPlanDto> CreatePlanAsync(SubscriptionPlanRequestDto request, CancellationToken cancellationToken = default)
        {
            ValidateRequest(request);
            await EnsureNameFreeAsync(request.Name, Guid.Empty, cancellationToken);

            var plan = new SubscriptionPlan { Id = Guid.NewGuid() };
            ApplyRequest(plan, request);

            await _planRepository.AddAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(plan);
        }

        public async Task<SubscriptionPlanDto> UpdatePlanAsync(Guid planId, SubscriptionPlanRequestDto request, CancellationToken cancellationToken = default)
        {
            var plan = await GetExistingAsync(planId, cancellationToken);
            ValidateRequest(request);
            await EnsureNameFreeAsync(request.Name, planId, cancellationToken);

            ApplyRequest(plan, request);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(plan);
        }

        public async Task DeletePlanAsync(Guid planId, CancellationToken cancellationToken = default)
        {
            var plan = await GetExistingAsync(planId, cancellationToken);

            if (await _planRepository.IsInUseAsync(planId, cancellationToken))
            {
                throw new UserFriendlyException("Gói này đã được người dùng hoặc giao dịch sử dụng nên không thể xóa. Hãy đặt IsActive = false thay thế.");
            }

            _planRepository.Remove(plan);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task<SubscriptionPlan> GetExistingAsync(Guid planId, CancellationToken cancellationToken)
        {
            return await _planRepository.GetByIdAsync(planId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy gói đăng ký.");
        }

        private async Task EnsureNameFreeAsync(string name, Guid excludeId, CancellationToken cancellationToken)
        {
            if (await _planRepository.NameExistsAsync(name.Trim(), excludeId, cancellationToken))
            {
                throw new UserFriendlyException("Tên gói đã tồn tại (không phân biệt hoa thường).");
            }
        }

        // CHECK: Price >= 0, DurationDays > 0, StorageLimitMb > 0, AiCallLimit >= 0, ProjectLimit > 0 (khi có giá trị)
        private static void ValidateRequest(SubscriptionPlanRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength)
            {
                throw new UserFriendlyException($"Tên gói không được để trống và tối đa {MaxNameLength} ký tự.");
            }

            if (request.Price < 0)
            {
                throw new UserFriendlyException("Giá gói không được âm.");
            }

            if (request.DurationDays <= 0)
            {
                throw new UserFriendlyException("Số ngày hiệu lực phải lớn hơn 0.");
            }

            if (request.StorageLimitMb <= 0)
            {
                throw new UserFriendlyException("Giới hạn dung lượng phải lớn hơn 0.");
            }

            if (request.AiCallLimit is < 0)
            {
                throw new UserFriendlyException("Giới hạn lượt gọi AI không được âm. Để trống nghĩa là không giới hạn.");
            }

            if (request.ProjectLimit is <= 0)
            {
                throw new UserFriendlyException("Giới hạn số dự án phải lớn hơn 0. Để trống nghĩa là không giới hạn.");
            }
        }

        private static void ApplyRequest(SubscriptionPlan plan, SubscriptionPlanRequestDto request)
        {
            plan.Name = request.Name.Trim();
            plan.Price = request.Price;
            plan.DurationDays = request.DurationDays;
            plan.AiCallLimit = request.AiCallLimit;
            plan.StorageLimitMb = request.StorageLimitMb;
            plan.ProjectLimit = request.ProjectLimit;
            plan.IsActive = request.IsActive;
        }

        private static SubscriptionPlanDto ToDto(SubscriptionPlan plan)
        {
            return new SubscriptionPlanDto
            {
                Id = plan.Id,
                Name = plan.Name,
                Price = plan.Price,
                DurationDays = plan.DurationDays,
                AiCallLimit = plan.AiCallLimit,
                StorageLimitMb = plan.StorageLimitMb,
                ProjectLimit = plan.ProjectLimit,
                IsActive = plan.IsActive
            };
        }
    }
}
