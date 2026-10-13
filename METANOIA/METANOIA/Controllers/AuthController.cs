using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace METANOIA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IUserService _userService;

        public AuthController(IUserService userService)
        {
            _userService = userService;
        }

        [HttpPost("google")]
        public async Task<ActionResult<AuthResponseDto>> LoginWithGoogle(
            [FromBody] GoogleLoginRequestDto request,
            CancellationToken cancellationToken)
        {
            var result = await _userService.LoginWithGoogleAsync(request.IdToken, cancellationToken);
            return Ok(result);
        }

        // Đổi refresh token lấy access token mới (và refresh token mới, token cũ hết tác dụng). Không cần JWT vì access token
        // thường đã hết hạn lúc gọi; refresh token chính là thông tin xác thực. Sai/hết hạn/bị thu hồi thì 401 INVALID_REFRESH_TOKEN.
        [HttpPost("refresh")]
        public async Task<ActionResult<AuthResponseDto>> Refresh(
            [FromBody] RefreshRequestDto request,
            CancellationToken cancellationToken)
        {
            return Ok(await _userService.RefreshAsync(request.RefreshToken, cancellationToken));
        }

        // Thu hồi refresh token. allDevices = true thì đăng xuất mọi thiết bị. Luôn trả 204. Access token hiện tại vẫn dùng được
        // đến khi hết hạn (tối đa Jwt:ExpiryMinutes); client nên xóa nó ngay.
        [HttpPost("logout")]
        public async Task<IActionResult> Logout(
            [FromBody] LogoutRequestDto request,
            CancellationToken cancellationToken)
        {
            await _userService.LogoutAsync(request.RefreshToken, request.AllDevices, cancellationToken);
            return NoContent();
        }
    }
}
