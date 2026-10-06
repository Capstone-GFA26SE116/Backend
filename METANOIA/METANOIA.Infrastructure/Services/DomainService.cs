using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using DomainEntity = METANOIA.Domain.Entities.Domain;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class DomainService : IDomainService
    {
        private const int MaxNameLength = 50;

        private readonly IDomainRepository _domainRepository;
        private readonly IUnitOfWork _unitOfWork;

        public DomainService(IDomainRepository domainRepository, IUnitOfWork unitOfWork)
        {
            _domainRepository = domainRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<DomainDto>> GetDomainsAsync(CancellationToken cancellationToken = default)
        {
            var domains = await _domainRepository.GetAllAsync(cancellationToken);
            return domains.Select(ToDto).ToList();
        }

        public async Task<DomainDto> GetDomainAsync(Guid domainId, CancellationToken cancellationToken = default)
        {
            var domain = await GetExistingAsync(domainId, cancellationToken);
            return ToDto(domain);
        }

        public async Task<DomainDto> CreateDomainAsync(DomainRequestDto request, CancellationToken cancellationToken = default)
        {
            var name = await ValidateNameAsync(request, Guid.Empty, cancellationToken);

            var domain = new DomainEntity
            {
                Id = Guid.NewGuid(),
                Name = name,
                IsActive = request.IsActive
            };

            await _domainRepository.AddAsync(domain, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(domain);
        }

        public async Task<DomainDto> UpdateDomainAsync(Guid domainId, DomainRequestDto request, CancellationToken cancellationToken = default)
        {
            var domain = await GetExistingAsync(domainId, cancellationToken);
            domain.Name = await ValidateNameAsync(request, domainId, cancellationToken);
            domain.IsActive = request.IsActive;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(domain);
        }

        public async Task DeleteDomainAsync(Guid domainId, CancellationToken cancellationToken = default)
        {
            var domain = await GetExistingAsync(domainId, cancellationToken);

            if (await _domainRepository.IsInUseAsync(domainId, cancellationToken))
            {
                throw new UserFriendlyException(
                    "Domain đang được task hoặc hồ sơ người dùng sử dụng nên không thể xóa. Hãy đặt IsActive = false thay thế.");
            }

            _domainRepository.Remove(domain);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task<DomainEntity> GetExistingAsync(Guid domainId, CancellationToken cancellationToken)
        {
            return await _domainRepository.GetByIdAsync(domainId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy domain.");
        }

        private async Task<string> ValidateNameAsync(DomainRequestDto request, Guid excludeId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength)
            {
                throw new UserFriendlyException($"Tên domain không được để trống và tối đa {MaxNameLength} ký tự.");
            }

            var name = request.Name.Trim();
            if (await _domainRepository.NameExistsAsync(name, excludeId, cancellationToken))
            {
                throw new UserFriendlyException("Tên domain đã tồn tại (không phân biệt hoa thường).");
            }

            return name;
        }

        private static DomainDto ToDto(DomainEntity domain)
        {
            return new DomainDto
            {
                Id = domain.Id,
                Name = domain.Name,
                IsActive = domain.IsActive
            };
        }
    }
}
