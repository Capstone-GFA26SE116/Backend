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
        private readonly IGoogleCalendarSyncService _syncService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GoogleCalendarController> _logger;

        public GoogleCalendarController(
            IGoogleCalendarConnectionService connectionService,
            IGoogleCalendarEventService eventService,
            IGoogleCalendarSyncService syncService,
            IConfiguration configuration,
            ILogger<GoogleCalendarController> logger)
        {
            _connectionService = connectionService;
            _eventService = eventService;
            _syncService = syncService;
            _configuration = configuration;
            _logger = logger;
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
            return Ok(new { authorizationUrl = _connectionService.BuildAuthorizationUrl(CurrentUserId) });
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
                return RedirectToFrontend("denied", "Bạn đã từ chối cấp quyền truy cập Google Calendar.");
            }

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            {
                return RedirectToFrontend("error", "Thiếu tham số code hoặc state từ Google.");
            }

            try
            {
                await _connectionService.ConnectAsync(code, state, cancellationToken);
            }
            catch (UserFriendlyException ex)
            {
                return RedirectToFrontend("error", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google Calendar callback failed");
                return RedirectToFrontend("error", $"Lỗi hệ thống: {ex.GetType().Name} - {ex.Message}");
            }

            return RedirectToFrontend("connected", null);
        }

        [Authorize]
        [HttpPost("sync")]
        public async Task<ActionResult<GoogleCalendarSyncResultDto>> Sync(CancellationToken cancellationToken)
        {
            return Ok(await _syncService.SyncAsync(CurrentUserId, cancellationToken));
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

        private IActionResult RedirectToFrontend(string status, string? message)
        {
            var baseUrl = _configuration["Frontend:CalendarConnectedRedirectUri"]
                ?? throw new InvalidOperationException("Cấu hình 'Frontend:CalendarConnectedRedirectUri' chưa được thiết lập.");

            var query = $"calendar={status}";
            if (message is not null)
            {
                query += $"&message={Uri.EscapeDataString(message)}";
            }

            var separator = baseUrl.Contains('?') ? "&" : "?";
            return Redirect($"{baseUrl}{separator}{query}");
        }
    }
}
