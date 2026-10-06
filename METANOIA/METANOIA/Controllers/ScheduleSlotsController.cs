using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Authorize]
    [ApiController]
    public class ScheduleSlotsController : ControllerBase
    {
        private readonly IScheduleSlotService _slotService;
        private readonly IScheduleChangeLogService _changeLogService;

        public ScheduleSlotsController(IScheduleSlotService slotService, IScheduleChangeLogService changeLogService)
        {
            _slotService = slotService;
            _changeLogService = changeLogService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet("api/tasks/{taskId:guid}/schedule-slots")]
        public async Task<ActionResult<IReadOnlyList<ScheduleSlotDto>>> GetSlots(Guid taskId, CancellationToken cancellationToken)
        {
            return Ok(await _slotService.GetSlotsAsync(CurrentUserId, taskId, cancellationToken));
        }

        [HttpPost("api/tasks/{taskId:guid}/schedule-slots")]
        public async Task<ActionResult<ScheduleSlotDto>> CreateSlot(
            Guid taskId,
            [FromBody] ScheduleSlotRequestDto request,
            CancellationToken cancellationToken)
        {
            var created = await _slotService.CreateSlotAsync(CurrentUserId, taskId, request, cancellationToken);
            return CreatedAtAction(nameof(GetSlot), new { slotId = created.Id }, created);
        }

        [HttpGet("api/schedule-slots/{slotId:guid}")]
        public async Task<ActionResult<ScheduleSlotDto>> GetSlot(Guid slotId, CancellationToken cancellationToken)
        {
            return Ok(await _slotService.GetSlotAsync(CurrentUserId, slotId, cancellationToken));
        }

        [HttpPut("api/schedule-slots/{slotId:guid}")]
        public async Task<ActionResult<ScheduleSlotDto>> UpdateSlot(
            Guid slotId,
            [FromBody] ScheduleSlotRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _slotService.UpdateSlotAsync(CurrentUserId, slotId, request, cancellationToken));
        }

        // Hủy slot (không xóa thật) để giữ lịch sử thay đổi
        [HttpDelete("api/schedule-slots/{slotId:guid}")]
        public async Task<ActionResult<ScheduleSlotDto>> CancelSlot(Guid slotId, CancellationToken cancellationToken)
        {
            return Ok(await _slotService.CancelSlotAsync(CurrentUserId, slotId, cancellationToken));
        }

        [HttpGet("api/schedule-slots/{slotId:guid}/changes")]
        public async Task<ActionResult<IReadOnlyList<ScheduleChangeLogDto>>> GetChanges(Guid slotId, CancellationToken cancellationToken)
        {
            return Ok(await _changeLogService.GetChangesAsync(CurrentUserId, slotId, cancellationToken));
        }

        [HttpPost("api/schedule-slots/{slotId:guid}/changes")]
        public async Task<ActionResult<ScheduleChangeLogDto>> CreateChange(
            Guid slotId,
            [FromBody] ScheduleChangeLogRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _changeLogService.CreateChangeAsync(CurrentUserId, slotId, request, cancellationToken));
        }

        [HttpGet("api/schedule-change-logs/{logId:guid}")]
        public async Task<ActionResult<ScheduleChangeLogDto>> GetChange(Guid logId, CancellationToken cancellationToken)
        {
            return Ok(await _changeLogService.GetChangeAsync(CurrentUserId, logId, cancellationToken));
        }
    }
}
