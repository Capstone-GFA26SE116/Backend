using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Authorize]
    [Route("api/clients")]
    [ApiController]
    public class ClientsController : ControllerBase
    {
        private readonly IClientService _clientService;

        public ClientsController(IClientService clientService)
        {
            _clientService = clientService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<ClientDto>>> GetClients(CancellationToken cancellationToken)
        {
            return Ok(await _clientService.GetClientsAsync(CurrentUserId, cancellationToken));
        }

        [HttpGet("{clientId:guid}")]
        public async Task<ActionResult<ClientDto>> GetClient(Guid clientId, CancellationToken cancellationToken)
        {
            return Ok(await _clientService.GetClientAsync(CurrentUserId, clientId, cancellationToken));
        }

        [HttpPost]
        public async Task<ActionResult<ClientDto>> CreateClient(
            [FromBody] ClientRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _clientService.CreateClientAsync(CurrentUserId, request, cancellationToken);
            return CreatedAtAction(nameof(GetClient), new { clientId = created.Id }, created);
        }

        [HttpPut("{clientId:guid}")]
        public async Task<ActionResult<ClientDto>> UpdateClient(
            Guid clientId,
            [FromBody] ClientRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _clientService.UpdateClientAsync(CurrentUserId, clientId, request, cancellationToken));
        }

        [HttpDelete("{clientId:guid}")]
        public async Task<IActionResult> DeleteClient(Guid clientId, CancellationToken cancellationToken)
        {
            await _clientService.DeleteClientAsync(CurrentUserId, clientId, cancellationToken);
            return NoContent();
        }
    }
}
