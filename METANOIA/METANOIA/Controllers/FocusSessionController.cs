using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace METANOIA.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class FocusSessionController(IFocusSessionService focusService) : ControllerBase
{
    [HttpPost("start")]
    [ProducesResponseType(typeof(FocusStartResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartSession([FromBody] FocusStartRequest request)
    {
        try
        {
            return StatusCode(StatusCodes.Status201Created,
                await focusService.StartSessionAsync(GetUserId(), request));
        }
        catch (FocusConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
    }

    [HttpPost("{sessionId:guid}/stop")]
    [ProducesResponseType(typeof(FocusStopResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StopSession(Guid sessionId, [FromBody] FocusStopRequest request)
    {
        return Ok(await focusService.StopSessionAsync(GetUserId(), sessionId, request));
    }

    [HttpGet("current")]
    [ProducesResponseType(typeof(FocusCurrentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetCurrentSession()
    {
        var result = await focusService.GetCurrentSessionAsync(GetUserId());
        return result is null ? NoContent() : Ok(result);
    }

    [HttpPost("{sessionId:guid}/confirm")]
    public async Task<IActionResult> ConfirmSession(Guid sessionId, [FromBody] FocusConfirmRequest request)
    {
        await focusService.ConfirmSessionAsync(GetUserId(), sessionId, request);
        return Ok(new { message = "Session confirmed successfully." });
    }

    [HttpPost("self-report")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> SelfReport([FromBody] FocusSelfReportRequest request)
    {
        var sessionId = await focusService.SelfReportSessionAsync(GetUserId(), request);
        return StatusCode(StatusCodes.Status201Created, new { sessionId });
    }

    [HttpGet("{slotId:guid}/time-summary")]
    [ProducesResponseType(typeof(FocusTimeSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTimeSummary(Guid slotId)
    {
        return Ok(await focusService.GetTimeSummaryAsync(GetUserId(), slotId));
    }

    private Guid GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var userId))
            throw new UnauthorizedAccessException("User identifier is missing or invalid in token.");
        return userId;
    }
}
