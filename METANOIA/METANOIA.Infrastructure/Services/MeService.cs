using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class MeService : IMeService
    {
        private const int MinMinutes = 0;
        private const int MaxMinutes = 120;
        private const decimal ProductiveScore = 0.800m;
        private const decimal WorkingScore = 0.500m;
        private const decimal OffHoursScore = 0.200m;
        private const string NotConnected = "NotConnected";

        // KBias khởi tạo theo mức hay làm lố
        private static readonly Dictionary<string, decimal> OverrunKBias = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Rarely"] = 1.100m,
            ["Sometimes"] = 1.250m,
            ["Often"] = 1.500m,
            ["Always"] = 1.800m
        };

        private readonly IUserSetupRepository _setup;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IGoogleCalendarConnectionService _googleConnection;
        private readonly IRefreshTokenRepository _refreshTokens;

        public MeService(
            IUserSetupRepository setup,
            IUnitOfWork unitOfWork,
            IGoogleCalendarConnectionService googleConnection,
            IRefreshTokenRepository refreshTokens)
        {
            _refreshTokens = refreshTokens;
            _setup = setup;
            _unitOfWork = unitOfWork;
            _googleConnection = googleConnection;
        }

        public async Task<MeDto> GetMeAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await GetUserAsync(userId, cancellationToken);
            return await ToDtoAsync(user, cancellationToken);
        }

        public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await GetUserAsync(userId, cancellationToken);
            user.DeletedAt = DateTime.UtcNow;

            // Thu hồi mọi phiên đăng nhập (refresh token) của tài khoản
            foreach (var token in await _refreshTokens.GetActiveByUserAsync(userId, user.DeletedAt.Value, cancellationToken))
            {
                token.RevokedAt = user.DeletedAt;
            }

            // Cùng DbContext nên các thay đổi trên được lưu chung với việc xóa liên kết Google (nếu có)
            await _googleConnection.DisconnectAsync(userId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<MeDto> UpdateSettingsAsync(Guid userId, UpdateSettingsRequestDto request, CancellationToken cancellationToken = default)
        {
            var user = await GetUserAsync(userId, cancellationToken);

            ValidateWorkHours(request.WorkDayStart, request.WorkDayEnd);
            ValidateMinutes(request.DefaultBufferMinutes, "Thời gian đệm mặc định");
            ValidateMinutes(request.ReminderLeadMinutes, "Thời gian nhắc trước");
            var timeZone = ValidateTimeZone(request.TimeZone);

            user.WorkDayStart = request.WorkDayStart;
            user.WorkDayEnd = request.WorkDayEnd;
            user.WorksOnWeekend = request.WorksOnWeekend;
            user.DefaultBufferMinutes = request.DefaultBufferMinutes;
            user.ReminderLeadMinutes = request.ReminderLeadMinutes;
            user.TimeZone = timeZone;

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return await ToDtoAsync(user, cancellationToken);
        }

        public async Task<MeDto> CompleteOnboardingAsync(Guid userId, OnboardingRequestDto request, CancellationToken cancellationToken = default)
        {
            var user = await GetUserAsync(userId, cancellationToken);

            ValidateWorkHours(request.WorkDayStart, request.WorkDayEnd);

            if (request.OverrunLevel is null || !OverrunKBias.TryGetValue(request.OverrunLevel, out var kbias))
            {
                throw new UserFriendlyException("Mức hay làm lố không hợp lệ. Chọn một trong: Rarely, Sometimes, Often, Always.");
            }

            var productiveHours = (request.ProductiveHours ?? new List<int>()).Distinct().ToList();
            if (productiveHours.Any(h => h < 0 || h > 23))
            {
                throw new UserFriendlyException("Giờ làm hiệu quả phải nằm trong khoảng 0 đến 23.");
            }

            var domainIds = (request.DomainIds ?? new List<Guid>()).Distinct().ToList();
            var activeDomainIds = (await _setup.GetActiveDomainIdsAsync(domainIds, cancellationToken)).ToHashSet();
            if (domainIds.Any(id => !activeDomainIds.Contains(id)))
            {
                throw new UserFriendlyException("Có loại công việc không tồn tại hoặc đang bị tắt.");
            }

            user.WorkDayStart = request.WorkDayStart;
            user.WorkDayEnd = request.WorkDayEnd;
            user.WorksOnWeekend = request.WorksOnWeekend;

            await ApplyCircadianScoresAsync(userId, request.WorkDayStart, request.WorkDayEnd, productiveHours.ToHashSet(), cancellationToken);
            await ApplyDomainProfilesAsync(userId, domainIds, kbias, cancellationToken);

            user.OnboardingCompleted = true;

            // Mọi thay đổi ở trên nằm trong một lần SaveChanges = một transaction
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return await ToDtoAsync(user, cancellationToken);
        }

        private async Task ApplyCircadianScoresAsync(
            Guid userId, TimeOnly start, TimeOnly end, HashSet<int> productiveHours, CancellationToken cancellationToken)
        {
            var existing = (await _setup.GetCircadianProfilesAsync(userId, cancellationToken))
                .ToDictionary(p => (int)p.HourOfDay);

            var startMinute = start.Hour * 60 + start.Minute;
            var endMinute = end.Hour * 60 + end.Minute;
            var now = DateTime.UtcNow;
            var created = new List<UserCircadianProfile>();

            for (var hour = 0; hour < UserInitializationService.HoursPerDay; hour++)
            {
                var score = ScoreForHour(hour, startMinute, endMinute, productiveHours);

                if (existing.TryGetValue(hour, out var profile))
                {
                    profile.Score = score;
                    profile.Source = UserInitializationService.OnboardingSource;
                    profile.UpdatedAt = now;
                }
                else
                {
                    created.Add(new UserCircadianProfile
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        HourOfDay = (short)hour,
                        Score = score,
                        Source = UserInitializationService.OnboardingSource,
                        UpdatedAt = now
                    });
                }
            }

            if (created.Count > 0)
            {
                await _setup.AddCircadianProfilesAsync(created, cancellationToken);
            }
        }

        // Giờ hiệu quả được ưu tiên; còn lại phân biệt trong và ngoài khung giờ làm việc
        private static decimal ScoreForHour(int hour, int startMinute, int endMinute, HashSet<int> productiveHours)
        {
            if (productiveHours.Contains(hour))
            {
                return ProductiveScore;
            }

            var inWorkingHours = hour * 60 < endMinute && (hour + 1) * 60 > startMinute;
            return inWorkingHours ? WorkingScore : OffHoursScore;
        }

        // Chưa có hồ sơ thì tạo. Đã có và chưa học gì (SampleCount = 0) thì lấy câu trả lời mới nhất;
        // đã có dữ liệu học thật (SampleCount > 0) thì giữ nguyên KBias đã học, không bị onboarding ghi đè.
        private async Task ApplyDomainProfilesAsync(Guid userId, List<Guid> domainIds, decimal kbias, CancellationToken cancellationToken)
        {
            var existing = (await _setup.GetDomainProfilesAsync(userId, cancellationToken)).ToDictionary(p => p.DomainId);
            var now = DateTime.UtcNow;
            var created = new List<UserDomainProfile>();

            foreach (var domainId in domainIds)
            {
                if (existing.TryGetValue(domainId, out var profile))
                {
                    if (profile.SampleCount == 0)
                    {
                        profile.Kbias = kbias;
                        profile.UpdatedAt = now;
                    }
                }
                else
                {
                    created.Add(new UserDomainProfile
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        DomainId = domainId,
                        Kbias = kbias,
                        SampleCount = 0,
                        UpdatedAt = now
                    });
                }
            }

            if (created.Count > 0)
            {
                await _setup.AddDomainProfilesAsync(created, cancellationToken);
            }
        }

        private async Task<User> GetUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            var user = await _setup.GetUserAsync(userId, cancellationToken);
            if (user is null || user.DeletedAt is not null)
            {
                throw new NotFoundException("Không tìm thấy người dùng.");
            }

            return user;
        }

        private async Task<MeDto> ToDtoAsync(User user, CancellationToken cancellationToken)
        {
            var subscription = await _setup.GetActiveSubscriptionAsync(user.Id, cancellationToken);
            var googleStatus = await _setup.GetGoogleConnectionStatusAsync(user.Id, cancellationToken);

            return new MeDto
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role.Name,
                TimeZone = user.TimeZone,
                WorkDayStart = user.WorkDayStart,
                WorkDayEnd = user.WorkDayEnd,
                WorksOnWeekend = user.WorksOnWeekend,
                DefaultBufferMinutes = user.DefaultBufferMinutes,
                ReminderLeadMinutes = user.ReminderLeadMinutes,
                OnboardingCompleted = user.OnboardingCompleted,
                GoogleCalendarStatus = string.IsNullOrWhiteSpace(googleStatus) ? NotConnected : googleStatus,
                CurrentSubscription = subscription is null
                    ? null
                    : new MeSubscriptionDto
                    {
                        SubscriptionId = subscription.Id,
                        PlanId = subscription.PlanId,
                        PlanName = subscription.Plan.Name,
                        EndAt = UtcTime.ToApi(subscription.EndAt)
                    }
            };
        }

        private static void ValidateWorkHours(TimeOnly start, TimeOnly end)
        {
            if (end <= start)
            {
                throw new UserFriendlyException("Giờ kết thúc làm việc phải lớn hơn giờ bắt đầu.");
            }
        }

        private static void ValidateMinutes(int value, string label)
        {
            if (value < MinMinutes || value > MaxMinutes)
            {
                throw new UserFriendlyException($"{label} phải nằm trong khoảng {MinMinutes} đến {MaxMinutes} phút.");
            }
        }

        private static string ValidateTimeZone(string? timeZone)
        {
            var value = timeZone?.Trim();
            if (string.IsNullOrEmpty(value) || value.Length > 50)
            {
                throw new UserFriendlyException("Múi giờ không hợp lệ (tối đa 50 ký tự).");
            }

            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(value);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new UserFriendlyException($"Múi giờ '{value}' không tồn tại. Ví dụ hợp lệ: Asia/Ho_Chi_Minh.");
            }

            return value;
        }
    }
}
