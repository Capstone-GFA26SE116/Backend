using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IDomainService
    {
        Task<IReadOnlyList<DomainDto>> GetDomainsAsync(CancellationToken cancellationToken = default);

        Task<DomainDto> GetDomainAsync(Guid domainId, CancellationToken cancellationToken = default);

        Task<DomainDto> CreateDomainAsync(DomainRequestDto request, CancellationToken cancellationToken = default);

        Task<DomainDto> UpdateDomainAsync(Guid domainId, DomainRequestDto request, CancellationToken cancellationToken = default);

        Task DeleteDomainAsync(Guid domainId, CancellationToken cancellationToken = default);
    }
}
