using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class AuthService : IUserService
    {
        private const string DefaultRoleName = "Freelancer";

        private readonly IUserRepository _userRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IGoogleTokenValidator _googleTokenValidator;
        private readonly IJwtTokenGenerator _jwtTokenGenerator;

        public AuthService(
            IUserRepository userRepository,
            IUnitOfWork unitOfWork,
            IGoogleTokenValidator googleTokenValidator,
            IJwtTokenGenerator jwtTokenGenerator)
        {
            _userRepository = userRepository;
            _unitOfWork = unitOfWork;
            _googleTokenValidator = googleTokenValidator;
            _jwtTokenGenerator = jwtTokenGenerator;
        }

        public async Task<AuthResponseDto> LoginWithGoogleAsync(string idToken, CancellationToken cancellationToken = default)
        {
            var googleUser = await _googleTokenValidator.ValidateAsync(idToken, cancellationToken);

            var user = await _userRepository.GetByGoogleSubjectIdAsync(googleUser.Subject, cancellationToken);

            if (user is null)
            {
                var existingByEmail = await _userRepository.GetByEmailAsync(googleUser.Email, cancellationToken);
                if (existingByEmail is not null)
                {
                    throw new UserFriendlyException("Email này đã được đăng ký bằng phương thức khác.");
                }

                var defaultRole = await _userRepository.GetRoleByNameAsync(DefaultRoleName, cancellationToken)
                    ?? throw new UserFriendlyException($"Role mặc định '{DefaultRoleName}' chưa được khởi tạo trong hệ thống.");

                user = new User
                {
                    Id = Guid.NewGuid(),
                    Email = googleUser.Email,
                    FullName = googleUser.FullName,
                    GoogleSubjectId = googleUser.Subject,
                    RoleId = defaultRole.Id,
                    Role = defaultRole
                };

                await _userRepository.AddAsync(user, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var (token, expiresAtUtc) = _jwtTokenGenerator.GenerateToken(user);

            return new AuthResponseDto
            {
                UserId = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role.Name,
                AccessToken = token,
                ExpiresAtUtc = expiresAtUtc
            };
        }
    }
}
