using System.Security.Claims;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Authorize]
    [Route("api/me")]
    [ApiController]
    public class MeController : ControllerBase
    {
        private readonly IMeService _meService;

        public MeController(IMeService meService)
        {
            _meService = meService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Thông tin user, trạng thái onboarding, gói hiện tại và trạng thái kết nối Google Calendar
        [HttpGet]
        public async Task<ActionResult<MeDto>> GetMe(CancellationToken cancellationToken)
        {
            return Ok(await _meService.GetMeAsync(CurrentUserId, cancellationToken));
        }

        // Xóa mềm tài khoản (DeletedAt) và hủy liên kết Google Calendar. Token hiện tại bị từ chối ngay sau đó
        [HttpDelete]
        public async Task<IActionResult> DeleteMe(CancellationToken cancellationToken)
        {
            await _meService.DeleteAsync(CurrentUserId, cancellationToken);
            return NoContent();
        }

        [HttpPut("settings")]
        public async Task<ActionResult<MeDto>> UpdateSettings(
            [FromBody] UpdateSettingsRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _meService.UpdateSettingsAsync(CurrentUserId, request, cancellationToken));
        }

        // Gọi lại nhiều lần được: ghi đè câu trả lời trước
        [HttpPost("onboarding")]
        public async Task<ActionResult<MeDto>> CompleteOnboarding(
            [FromBody] OnboardingRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _meService.CompleteOnboardingAsync(CurrentUserId, request, cancellationToken));
        }
    }
}
