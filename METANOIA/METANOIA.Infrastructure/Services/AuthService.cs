using System.Security.Cryptography;
using System.Text;
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class AuthService : IUserService
    {
        private const string DefaultRoleName = "Freelancer";
        private const int DefaultRefreshTokenDays = 30;

        // Hai request refresh song song với cùng một token (hai tab, bấm đúp) là chuyện thường: trong khoảng này
        // token đã xoay vòng chỉ bị từ chối, chưa coi là bị đánh cắp.
        private static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(10);

        private readonly IUserRepository _userRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IGoogleTokenValidator _googleTokenValidator;
        private readonly IJwtTokenGenerator _jwtTokenGenerator;
        private readonly IUserInitializationService _userInitializationService;
        private readonly int _refreshTokenDays;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            IUserRepository userRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IUnitOfWork unitOfWork,
            IGoogleTokenValidator googleTokenValidator,
            IJwtTokenGenerator jwtTokenGenerator,
            IUserInitializationService userInitializationService,
            IConfiguration configuration,
            ILogger<AuthService> logger)
        {
            _userRepository = userRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _unitOfWork = unitOfWork;
            _googleTokenValidator = googleTokenValidator;
            _jwtTokenGenerator = jwtTokenGenerator;
            _userInitializationService = userInitializationService;
            _logger = logger;
            _refreshTokenDays = int.TryParse(configuration["Jwt:RefreshTokenDays"], out var days) && days > 0
                ? days
                : DefaultRefreshTokenDays;
        }

        public async Task<AuthResponseDto> LoginWithGoogleAsync(string idToken, CancellationToken cancellationToken = default)
        {
            var googleUser = await _googleTokenValidator.ValidateAsync(idToken, cancellationToken);

            var user = await _userRepository.GetByGoogleSubjectIdAsync(googleUser.Subject, cancellationToken);

            if (user is not null && user.DeletedAt is not null)
            {
                throw new UserFriendlyException("Tài khoản này đã bị xóa.");
            }

            AuthResponseDto response;

            if (user is null)
            {
                var existingByEmail = await _userRepository.GetByEmailAsync(googleUser.Email, cancellationToken);
                if (existingByEmail is not null)
                {
                    // Tài khoản Admin tạo sẵn cho email này: chủ nhân đăng nhập lần đầu thì nhận tài khoản (Google đã xác minh email)
                    if (existingByEmail.DeletedAt is not null
                        || !existingByEmail.GoogleSubjectId.StartsWith(AccountConstants.PendingSubjectPrefix, StringComparison.Ordinal))
                    {
                        throw new UserFriendlyException("Email này đã được đăng ký bằng phương thức khác.");
                    }

                    existingByEmail.GoogleSubjectId = googleUser.Subject;
                    user = existingByEmail;
                }
            }

            if (user is null)
            {
                var defaultRole = await _userRepository.GetRoleByNameAsync(DefaultRoleName, cancellationToken)
                    ?? throw new UserFriendlyException($"Role mặc định '{DefaultRoleName}' chưa được khởi tạo trong hệ thống.");

                user = new User
                {
                    Id = Guid.NewGuid(),
                    Email = googleUser.Email,
                    FullName = googleUser.FullName,
                    GoogleSubjectId = googleUser.Subject,
                    RoleId = defaultRole.Id,
                    Role = defaultRole,
                    TimeZone = "Asia/Ho_Chi_Minh",
                    WorkDayStart = new TimeOnly(8, 0),
                    WorkDayEnd = new TimeOnly(18, 0),
                    WorksOnWeekend = false,
                    DefaultBufferMinutes = 15,
                    ReminderLeadMinutes = 15,
                    OnboardingCompleted = false
                };

                await _userRepository.AddAsync(user, cancellationToken);
                (response, _) = await IssueTokensAsync(user, cancellationToken);

                // Tạo User cùng project "Cá nhân", gói Miễn phí, 24 dòng circadian và refresh token đầu tiên trong CÙNG một lần lưu:
                // lỗi ở bất kỳ bước nào thì không có User nào được tạo.
                await _userInitializationService.InitializeNewUserAsync(user.Id, cancellationToken);
            }
            else
            {
                (response, _) = await IssueTokensAsync(user, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return response;
        }

        public async Task<AuthResponseDto> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new UserFriendlyException("Thiếu refresh token.");
            }

            var now = DateTime.UtcNow;
            var stored = await _refreshTokenRepository.GetByHashAsync(Hash(refreshToken), cancellationToken)
                ?? throw new InvalidRefreshTokenException("Refresh token không hợp lệ. Vui lòng đăng nhập lại.");

            if (stored.RevokedAt is { } revokedAt)
            {
                // Token đã được đổi lấy token mới mà vẫn có người đem ra dùng, quá thời gian ân hạn: coi là bị đánh cắp
                if (stored.ReplacedById is not null && now - revokedAt > ReuseGrace)
                {
                    _logger.LogWarning("Refresh token đã xoay vòng bị dùng lại, thu hồi toàn bộ phiên của user {UserId}.", stored.UserId);
                    await RevokeAllAsync(stored.UserId, now, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                throw new InvalidRefreshTokenException("Refresh token đã bị thu hồi. Vui lòng đăng nhập lại.");
            }

            if (stored.ExpiresAt <= now)
            {
                throw new InvalidRefreshTokenException("Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
            }

            var user = await _userRepository.GetByIdAsync(stored.UserId, cancellationToken);
            if (user is null || user.DeletedAt is not null)
            {
                throw new InvalidRefreshTokenException("Tài khoản không tồn tại hoặc đã bị xóa.");
            }

            // Xoay vòng: token cũ bị thu hồi và trỏ sang token mới, lưu cùng lúc
            var (response, newToken) = await IssueTokensAsync(user, cancellationToken);
            stored.RevokedAt = now;
            stored.ReplacedById = newToken.Id;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return response;
        }

        public async Task LogoutAsync(string refreshToken, bool allDevices, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new UserFriendlyException("Thiếu refresh token.");
            }

            var stored = await _refreshTokenRepository.GetByHashAsync(Hash(refreshToken), cancellationToken);
            if (stored is null)
            {
                // Không tiết lộ token có tồn tại hay không; đăng xuất vốn là thao tác idempotent
                return;
            }

            var now = DateTime.UtcNow;
            if (allDevices)
            {
                await RevokeAllAsync(stored.UserId, now, cancellationToken);
            }

            if (stored.RevokedAt is null)
            {
                stored.RevokedAt = now;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // Chuẩn bị access token + refresh token mới cho user (refresh token được thêm vào hàng chờ, người gọi tự lưu)
        private async Task<(AuthResponseDto Response, RefreshToken Token)> IssueTokensAsync(User user, CancellationToken cancellationToken)
        {
            var (accessToken, accessExpiresAtUtc) = _jwtTokenGenerator.GenerateToken(user);

            var rawRefreshToken = GenerateRawToken();
            var now = DateTime.UtcNow;
            var entity = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = Hash(rawRefreshToken),
                CreatedAt = now,
                ExpiresAt = now.AddDays(_refreshTokenDays)
            };
            await _refreshTokenRepository.AddAsync(entity, cancellationToken);

            var response = new AuthResponseDto
            {
                UserId = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role.Name,
                AccessToken = accessToken,
                ExpiresAtUtc = accessExpiresAtUtc,
                RefreshToken = rawRefreshToken,
                RefreshTokenExpiresAtUtc = entity.ExpiresAt
            };

            return (response, entity);
        }

        private async Task RevokeAllAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
        {
            foreach (var token in await _refreshTokenRepository.GetActiveByUserAsync(userId, now, cancellationToken))
            {
                token.RevokedAt = now;
            }
        }

        // 32 byte ngẫu nhiên mã hóa base64 an toàn cho URL
        private static string GenerateRawToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        // Database chỉ giữ bản băm: lộ database cũng không dùng lại được token
        internal static string Hash(string rawToken)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
        }
    }
}
