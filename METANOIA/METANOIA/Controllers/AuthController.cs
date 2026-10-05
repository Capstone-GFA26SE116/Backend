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
    }
}
