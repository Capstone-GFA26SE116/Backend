using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Authorize]
    [Route("api/calendar")]
    [ApiController]
    public class CalendarController : ControllerBase
    {
        private readonly ICalendarService _calendarService;

        public CalendarController(ICalendarService calendarService)
        {
            _calendarService = calendarService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Gộp slot của METANOIA với event Google (giờ bận) trong khoảng [from, to), tối đa 100 ngày.
        // Event Google lấy từ bản đã đồng bộ, nên gọi POST /api/google-calendar/sync trước nếu cần dữ liệu mới nhất.
        [HttpGet]
        public async Task<ActionResult<CalendarDto>> GetCalendar(
            [FromQuery] DateTimeOffset from,
            [FromQuery] DateTimeOffset to,
            [FromQuery] bool includeCancelled,
            CancellationToken cancellationToken)
        {
            return Ok(await _calendarService.GetCalendarAsync(CurrentUserId, from, to, includeCancelled, cancellationToken));
        }
    }
}
