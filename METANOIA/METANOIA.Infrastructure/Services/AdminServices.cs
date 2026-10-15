using System.Text.Json;
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    internal static class Paging
    {
        public const int DefaultPageSize = 20;
        public const int MaxPageSize = 100;

        public static (int Page, int PageSize) Normalize(int page, int pageSize)
        {
            if (page < 1)
            {
                throw new UserFriendlyException("page phải từ 1 trở lên.");
            }

            if (pageSize < 1 || pageSize > MaxPageSize)
            {
                throw new UserFriendlyException($"pageSize phải nằm trong khoảng 1 đến {MaxPageSize}.");
            }

            return (page, pageSize);
        }
    }

    public class AdminUserService : IAdminUserService
    {
        private const string AdminRoleName = "Admin";
        private const string DefaultRoleName = "Freelancer";
        private const int MaxEmailLength = 255;
        private const int MaxNameLength = 100;

        private readonly IAdminUserRepository _repository;
        private readonly IUserRepository _users;
        private readonly IUserSetupRepository _setup;
        private readonly IUserInitializationService _initialization;
        private readonly IMeService _meService;
        private readonly IUnitOfWork _unitOfWork;

        public AdminUserService(
            IAdminUserRepository repository,
            IUserRepository users,
            IUserSetupRepository setup,
            IUserInitializationService initialization,
            IMeService meService,
            IUnitOfWork unitOfWork)
        {
            _repository = repository;
            _users = users;
            _setup = setup;
            _initialization = initialization;
            _meService = meService;
            _unitOfWork = unitOfWork;
        }

        public Task<PagedResultDto<AdminUserListItemDto>> SearchAsync(
            string? search, string? role, bool includeDeleted, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var (p, size) = Paging.Normalize(page, pageSize);
            return _repository.SearchAsync(search, role, includeDeleted, p, size, cancellationToken);
        }

        public async Task<AdminUserDetailDto> GetAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _repository.GetDetailAsync(userId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy người dùng.");
        }

        public async Task<AdminUserDetailDto> CreateAsync(AdminUserRequestDto request, CancellationToken cancellationToken = default)
        {
            var email = NormalizeEmail(request.Email ?? throw new UserFriendlyException("Email không được để trống."));
            var fullName = NormalizeName(request.FullName ?? throw new UserFriendlyException("Họ tên không được để trống."));

            if (await _repository.EmailInUseAsync(email, Guid.Empty, cancellationToken))
            {
                throw new UserFriendlyException("Email này đã được dùng bởi một tài khoản khác (kể cả tài khoản đã xóa).");
            }

            var role = await GetRoleAsync(request.Role ?? DefaultRoleName, cancellationToken);
            var start = request.WorkDayStart ?? new TimeOnly(8, 0);
            var end = request.WorkDayEnd ?? new TimeOnly(18, 0);
            var buffer = request.DefaultBufferMinutes ?? 15;
            var reminder = request.ReminderLeadMinutes ?? 15;
            UserSettingsValidator.ValidateWorkHours(start, end);
            UserSettingsValidator.ValidateMinutes(buffer, "Thời gian đệm mặc định");
            UserSettingsValidator.ValidateMinutes(reminder, "Thời gian nhắc trước");

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                FullName = fullName,
                // Giữ chỗ cho đến khi chủ nhân đăng nhập Google lần đầu bằng đúng email này
                GoogleSubjectId = AccountConstants.PendingSubjectPrefix + Guid.NewGuid().ToString("N"),
                RoleId = role.Id,
                Role = role,
                TimeZone = request.TimeZone is null ? "Asia/Ho_Chi_Minh" : UserSettingsValidator.ValidateTimeZone(request.TimeZone),
                WorkDayStart = start,
                WorkDayEnd = end,
                WorksOnWeekend = request.WorksOnWeekend ?? false,
                DefaultBufferMinutes = buffer,
                ReminderLeadMinutes = reminder,
                OnboardingCompleted = false,
                CreatedAt = DateTime.UtcNow
            };

            await _users.AddAsync(user, cancellationToken);

            // Cùng một lần lưu với project "Cá nhân", gói Miễn phí và dữ liệu circadian
            await _initialization.InitializeNewUserAsync(user.Id, cancellationToken);

            return await GetAsync(user.Id, cancellationToken);
        }

        public async Task<AdminUserDetailDto> UpdateAsync(Guid userId, AdminUserRequestDto request, CancellationToken cancellationToken = default)
        {
            var user = await GetExistingAsync(userId, cancellationToken);
            if (user.DeletedAt is not null)
            {
                throw new UserFriendlyException("Tài khoản đã bị xóa. Hãy khôi phục trước khi sửa.");
            }

            if (request.Email is not null)
            {
                var email = NormalizeEmail(request.Email);
                if (await _repository.EmailInUseAsync(email, user.Id, cancellationToken))
                {
                    throw new UserFriendlyException("Email này đã được dùng bởi một tài khoản khác.");
                }

                user.Email = email;
            }

            if (request.FullName is not null)
            {
                user.FullName = NormalizeName(request.FullName);
            }

            if (request.Role is not null && !string.Equals(request.Role.Trim(), user.Role.Name, StringComparison.OrdinalIgnoreCase))
            {
                var role = await GetRoleAsync(request.Role, cancellationToken);
                if (user.Role.Name == AdminRoleName && await _repository.CountActiveAdminsAsync(cancellationToken) <= 1)
                {
                    throw new UserFriendlyException("Không thể hạ quyền Admin cuối cùng của hệ thống.");
                }

                user.RoleId = role.Id;
                user.Role = role;
            }

            // Chỉ kiểm tra giờ làm khi request có đổi giờ (giờ còn lại lấy từ dữ liệu hiện có)
            if (request.WorkDayStart is not null || request.WorkDayEnd is not null)
            {
                var start = request.WorkDayStart ?? user.WorkDayStart;
                var end = request.WorkDayEnd ?? user.WorkDayEnd;
                UserSettingsValidator.ValidateWorkHours(start, end);
                user.WorkDayStart = start;
                user.WorkDayEnd = end;
            }

            if (request.TimeZone is not null)
            {
                user.TimeZone = UserSettingsValidator.ValidateTimeZone(request.TimeZone);
            }

            if (request.WorksOnWeekend is { } weekend)
            {
                user.WorksOnWeekend = weekend;
            }

            if (request.DefaultBufferMinutes is { } buffer)
            {
                UserSettingsValidator.ValidateMinutes(buffer, "Thời gian đệm mặc định");
                user.DefaultBufferMinutes = buffer;
            }

            if (request.ReminderLeadMinutes is { } reminder)
            {
                UserSettingsValidator.ValidateMinutes(reminder, "Thời gian nhắc trước");
                user.ReminderLeadMinutes = reminder;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return await GetAsync(userId, cancellationToken);
        }

        public async Task DeleteAsync(Guid actingAdminId, Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await GetExistingAsync(userId, cancellationToken);
            if (user.DeletedAt is not null)
            {
                throw new NotFoundException("Không tìm thấy người dùng.");
            }

            if (user.Id == actingAdminId)
            {
                throw new UserFriendlyException("Không thể tự xóa tài khoản của mình ở đây. Hãy dùng DELETE /api/me.");
            }

            if (user.Role.Name == AdminRoleName && await _repository.CountActiveAdminsAsync(cancellationToken) <= 1)
            {
                throw new UserFriendlyException("Không thể xóa Admin cuối cùng của hệ thống.");
            }

            // Cùng cách xóa với DELETE /api/me: xóa mềm, thu hồi mọi phiên đăng nhập, hủy liên kết Google Calendar
            await _meService.DeleteAsync(userId, cancellationToken);
        }

        public async Task<AdminUserDetailDto> RestoreAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await GetExistingAsync(userId, cancellationToken);
            if (user.DeletedAt is null)
            {
                throw new UserFriendlyException("Tài khoản này chưa bị xóa.");
            }

            if (await _repository.EmailInUseAsync(user.Email, user.Id, cancellationToken))
            {
                throw new UserFriendlyException("Email của tài khoản này đã được dùng bởi một tài khoản khác nên không thể khôi phục.");
            }

            user.DeletedAt = null;

            // Bảo đảm đủ dữ liệu khởi tạo (idempotent) rồi lưu cùng lúc
            await _initialization.InitializeNewUserAsync(userId, cancellationToken);
            return await GetAsync(userId, cancellationToken);
        }

        private async Task<User> GetExistingAsync(Guid userId, CancellationToken cancellationToken)
        {
            return await _setup.GetUserAsync(userId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy người dùng.");
        }

        private async Task<Role> GetRoleAsync(string name, CancellationToken cancellationToken)
        {
            return await _users.GetRoleByNameAsync(name.Trim(), cancellationToken)
                ?? throw new UserFriendlyException($"Role '{name.Trim()}' không tồn tại.");
        }

        private static string NormalizeEmail(string value)
        {
            var email = value.Trim().ToLowerInvariant();
            if (email.Length == 0 || email.Length > MaxEmailLength || !email.Contains('@') || email.Contains(' '))
            {
                throw new UserFriendlyException("Email không hợp lệ.");
            }

            return email;
        }

        private static string NormalizeName(string value)
        {
            var name = value.Trim();
            if (name.Length == 0 || name.Length > MaxNameLength)
            {
                throw new UserFriendlyException($"Họ tên không được để trống và tối đa {MaxNameLength} ký tự.");
            }

            return name;
        }
    }

    public class AiUsageReportService : IAiUsageReportService
    {
        private const int DefaultRangeDays = 30;
        private const int MaxRangeDays = 366;

        private static readonly HashSet<string> Groupings = new() { "feature", "model", "day" };

        private readonly IAiUsageRepository _repository;

        public AiUsageReportService(IAiUsageRepository repository)
        {
            _repository = repository;
        }

        public async Task<AiUsageReportDto> GetReportAsync(
            string? groupBy, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default)
        {
            var grouping = string.IsNullOrWhiteSpace(groupBy) ? "feature" : groupBy.Trim().ToLowerInvariant();
            if (!Groupings.Contains(grouping))
            {
                throw new UserFriendlyException("groupBy không hợp lệ. Chọn một trong: feature, model, day.");
            }

            var end = to ?? DateTimeOffset.UtcNow;
            var start = from ?? end.AddDays(-DefaultRangeDays);
            if (end <= start)
            {
                throw new UserFriendlyException("Khoảng thời gian không hợp lệ: 'to' phải lớn hơn 'from'.");
            }

            if (end - start > TimeSpan.FromDays(MaxRangeDays))
            {
                throw new UserFriendlyException($"Khoảng thời gian tối đa {MaxRangeDays} ngày.");
            }

            var rows = await _repository.SummarizeAsync(grouping, start.UtcDateTime, end.UtcDateTime, cancellationToken);
            var total = await _repository.TotalAsync(start.UtcDateTime, end.UtcDateTime, cancellationToken);

            // Theo ngày thì sắp theo thời gian; theo feature / model thì dòng tốn kém nhất lên đầu
            var ordered = grouping == "day"
                ? rows.OrderBy(r => r.Key, StringComparer.Ordinal)
                : rows.OrderByDescending(r => r.Cost).ThenByDescending(r => r.Calls).ThenBy(r => r.Key, StringComparer.Ordinal);

            return new AiUsageReportDto
            {
                From = start,
                To = end,
                GroupBy = grouping,
                Total = total,
                Rows = ordered.ToList()
            };
        }
    }

    public class FocusTrackService : IFocusTrackService
    {
        private const int MaxTitleLength = 150;
        private const int MaxArtistLength = 100;
        private const int MaxUrlLength = 500;

        private readonly IFocusTrackRepository _repository;
        private readonly IUnitOfWork _unitOfWork;

        public FocusTrackService(IFocusTrackRepository repository, IUnitOfWork unitOfWork)
        {
            _repository = repository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<FocusTrackDto>> GetTracksAsync(bool? isActive, CancellationToken cancellationToken = default)
        {
            return (await _repository.GetAsync(isActive, cancellationToken)).Select(ToDto).ToList();
        }

        public async Task<FocusTrackDto> GetTrackAsync(Guid trackId, CancellationToken cancellationToken = default)
        {
            return ToDto(await GetExistingAsync(trackId, cancellationToken));
        }

        public async Task<FocusTrackDto> CreateTrackAsync(FocusTrackRequestDto request, CancellationToken cancellationToken = default)
        {
            Validate(request);

            var track = new FocusTrack { Id = Guid.NewGuid() };
            Apply(track, request);

            await _repository.AddAsync(track, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(track);
        }

        public async Task<FocusTrackDto> UpdateTrackAsync(Guid trackId, FocusTrackRequestDto request, CancellationToken cancellationToken = default)
        {
            var track = await GetExistingAsync(trackId, cancellationToken);
            Validate(request);

            Apply(track, request);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(track);
        }

        private async Task<FocusTrack> GetExistingAsync(Guid trackId, CancellationToken cancellationToken)
        {
            return await _repository.GetByIdAsync(trackId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy bài nhạc tập trung.");
        }

        private static void Validate(FocusTrackRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > MaxTitleLength)
            {
                throw new UserFriendlyException($"Tên bài không được để trống và tối đa {MaxTitleLength} ký tự.");
            }

            if (string.IsNullOrWhiteSpace(request.Artist) || request.Artist.Trim().Length > MaxArtistLength)
            {
                throw new UserFriendlyException($"Tên nghệ sĩ không được để trống và tối đa {MaxArtistLength} ký tự.");
            }

            var url = request.FileUrl?.Trim();
            if (string.IsNullOrEmpty(url) || url.Length > MaxUrlLength
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new UserFriendlyException($"FileUrl phải là đường dẫn http(s) hợp lệ, tối đa {MaxUrlLength} ký tự.");
            }

            if (request.DurationSeconds <= 0)
            {
                throw new UserFriendlyException("Thời lượng phải lớn hơn 0 giây.");
            }
        }

        private static void Apply(FocusTrack track, FocusTrackRequestDto request)
        {
            track.Title = request.Title.Trim();
            track.Artist = request.Artist.Trim();
            track.FileUrl = request.FileUrl.Trim();
            track.DurationSeconds = request.DurationSeconds;
            track.IsActive = request.IsActive;
        }

        private static FocusTrackDto ToDto(FocusTrack track) => new()
        {
            Id = track.Id,
            Title = track.Title,
            Artist = track.Artist,
            FileUrl = track.FileUrl,
            DurationSeconds = track.DurationSeconds,
            IsActive = track.IsActive
        };
    }

    public class AdminAuditLogService : IAdminAuditLogService
    {
        private readonly IAdminAuditLogRepository _repository;

        public AdminAuditLogService(IAdminAuditLogRepository repository)
        {
            _repository = repository;
        }

        public async Task<PagedResultDto<AdminAuditLogDto>> SearchAsync(
            Guid? adminId, string? targetType, Guid? targetId, string? action,
            DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var (p, size) = Paging.Normalize(page, pageSize);
            if (from is { } start && to is { } end && end <= start)
            {
                throw new UserFriendlyException("Khoảng thời gian không hợp lệ: 'to' phải lớn hơn 'from'.");
            }

            var (items, total) = await _repository.SearchAsync(
                adminId, targetType, targetId, action, from?.UtcDateTime, to?.UtcDateTime, p, size, cancellationToken);

            return new PagedResultDto<AdminAuditLogDto>
            {
                Page = p,
                PageSize = size,
                TotalCount = total,
                Items = items.Select(i => new AdminAuditLogDto
                {
                    Id = i.Log.Id,
                    AdminId = i.Log.AdminId,
                    AdminEmail = i.AdminEmail,
                    Action = i.Log.Action,
                    TargetType = i.Log.TargetType,
                    TargetId = i.Log.TargetId,
                    OldValue = ParseJson(i.Log.OldValue),
                    NewValue = ParseJson(i.Log.NewValue),
                    Reason = i.Log.Reason,
                    CreatedAt = UtcTime.ToApi(i.Log.CreatedAt)
                }).ToList()
            };
        }

        private static JsonElement? ParseJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Dữ liệu cũ không phải JSON hợp lệ: trả nguyên văn dạng chuỗi
                return JsonSerializer.SerializeToElement(json);
            }
        }
    }
}
