using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    // Danh mục domain dùng chung: mọi user đăng nhập được xem, chỉ Admin được thay đổi
    [Authorize]
    [Route("api/domains")]
    [ApiController]
    public class DomainsController : ControllerBase
    {
        private const string AdminRole = "Admin";

        private readonly IDomainService _domainService;

        public DomainsController(IDomainService domainService)
        {
            _domainService = domainService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<DomainDto>>> GetDomains(CancellationToken cancellationToken)
        {
            return Ok(await _domainService.GetDomainsAsync(cancellationToken));
        }

        [HttpGet("{domainId:guid}")]
        public async Task<ActionResult<DomainDto>> GetDomain(Guid domainId, CancellationToken cancellationToken)
        {
            return Ok(await _domainService.GetDomainAsync(domainId, cancellationToken));
        }

        [Authorize(Roles = AdminRole)]
        [HttpPost]
        public async Task<ActionResult<DomainDto>> CreateDomain([FromBody] DomainRequestDto request, CancellationToken cancellationToken)
        {
            var created = await _domainService.CreateDomainAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetDomain), new { domainId = created.Id }, created);
        }

        [Authorize(Roles = AdminRole)]
        [HttpPut("{domainId:guid}")]
        public async Task<ActionResult<DomainDto>> UpdateDomain(
            Guid domainId,
            [FromBody] DomainRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _domainService.UpdateDomainAsync(domainId, request, cancellationToken));
        }

        [Authorize(Roles = AdminRole)]
        [HttpDelete("{domainId:guid}")]
        public async Task<IActionResult> DeleteDomain(Guid domainId, CancellationToken cancellationToken)
        {
            await _domainService.DeleteDomainAsync(domainId, cancellationToken);
            return NoContent();
        }
    }
}
