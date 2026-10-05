using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Route("api/google-calendar")]
    [ApiController]
    public class GoogleCalendarController : ControllerBase
    {
        private readonly IGoogleCalendarConnectionService _connectionService;
        private readonly IGoogleCalendarEventService _eventService;

        public GoogleCalendarController(
            IGoogleCalendarConnectionService connectionService,
            IGoogleCalendarEventService eventService)
        {
            _connectionService = connectionService;
            _eventService = eventService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [Authorize]
        [HttpGet("status")]
        public async Task<ActionResult<GoogleCalendarStatusDto>> GetStatus(CancellationToken cancellationToken)
        {
            return Ok(await _connectionService.GetStatusAsync(CurrentUserId, cancellationToken));
        }

        [Authorize]
        [HttpGet("connect")]
        public IActionResult Connect()
        {
            return Redirect(_connectionService.BuildAuthorizationUrl(CurrentUserId));
        }

        [HttpGet("callback")]
        public async Task<IActionResult> Callback(
            [FromQuery] string? code,
            [FromQuery] string? state,
            [FromQuery] string? error,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(error))
            {
                throw new UserFriendlyException("Bạn đã từ chối cấp quyền truy cập Google Calendar.");
            }

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            {
                throw new UserFriendlyException("Thiếu tham số code hoặc state từ Google.");
            }

            await _connectionService.ConnectAsync(code, state, cancellationToken);
            return Ok(new { message = "Đã kết nối Google Calendar thành công." });
        }

        [Authorize]
        [HttpGet("events")]
        public async Task<ActionResult<IReadOnlyList<CalendarEventDto>>> GetEvents(
            [FromQuery] DateTimeOffset from,
            [FromQuery] DateTimeOffset to,
            CancellationToken cancellationToken)
        {
            return Ok(await _eventService.GetEventsAsync(CurrentUserId, from, to, cancellationToken));
        }

        [Authorize]
        [HttpGet("events/{eventId}")]
        public async Task<ActionResult<CalendarEventDto>> GetEvent(string eventId, CancellationToken cancellationToken)
        {
            return Ok(await _eventService.GetEventAsync(CurrentUserId, eventId, cancellationToken));
        }

        [Authorize]
        [HttpPost("events")]
        public async Task<ActionResult<CalendarEventDto>> CreateEvent(
            [FromBody] CalendarEventRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _eventService.CreateEventAsync(CurrentUserId, request, cancellationToken);
            return CreatedAtAction(nameof(GetEvent), new { eventId = created.Id }, created);
        }

        [Authorize]
        [HttpPut("events/{eventId}")]
        public async Task<ActionResult<CalendarEventDto>> UpdateEvent(
            string eventId,
            [FromBody] CalendarEventRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _eventService.UpdateEventAsync(CurrentUserId, eventId, request, cancellationToken));
        }

        [Authorize]
        [HttpDelete("events/{eventId}")]
        public async Task<IActionResult> DeleteEvent(string eventId, CancellationToken cancellationToken)
        {
            await _eventService.DeleteEventAsync(CurrentUserId, eventId, cancellationToken);
            return NoContent();
        }
    }
}
