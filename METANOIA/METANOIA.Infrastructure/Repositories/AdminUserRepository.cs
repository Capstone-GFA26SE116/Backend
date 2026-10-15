using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Infrastructure.Data;
using METANOIA.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class AdminUserRepository : IAdminUserRepository
    {
        private const string ActiveStatus = "Active";
        private const string NotConnected = "NotConnected";

        private readonly AppDbContext _context;

        public AdminUserRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<PagedResultDto<AdminUserListItemDto>> SearchAsync(
            string? search, string? role, bool includeDeleted, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var query = _context.Users.AsQueryable();

            if (!includeDeleted)
            {
                query = query.Where(u => u.DeletedAt == null);
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                query = query.Where(u => u.Role.Name == role);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(u => u.Email.ToLower().Contains(term) || u.FullName.ToLower().Contains(term));
            }

            var total = await query.CountAsync(cancellationToken);

            var rows = await query
                .OrderByDescending(u => u.CreatedAt)
                .ThenBy(u => u.Email)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new
                {
                    u.Id,
                    u.Email,
                    u.FullName,
                    Role = u.Role.Name,
                    u.OnboardingCompleted,
                    u.CreatedAt,
                    u.DeletedAt,
                    PlanName = _context.UserSubscriptions
                        .Where(s => s.UserId == u.Id && s.Status == ActiveStatus)
                        .Select(s => s.Plan.Name)
                        .FirstOrDefault(),
                    PlanEndAt = _context.UserSubscriptions
                        .Where(s => s.UserId == u.Id && s.Status == ActiveStatus)
                        .Select(s => s.EndAt)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            return new PagedResultDto<AdminUserListItemDto>
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = total,
                Items = rows.Select(r => new AdminUserListItemDto
                {
                    Id = r.Id,
                    Email = r.Email,
                    FullName = r.FullName,
                    Role = r.Role,
                    OnboardingCompleted = r.OnboardingCompleted,
                    CreatedAt = UtcTime.ToApi(r.CreatedAt),
                    DeletedAt = UtcTime.ToApi(r.DeletedAt),
                    CurrentPlanName = r.PlanName,
                    CurrentPlanEndAt = UtcTime.ToApi(r.PlanEndAt)
                }).ToList()
            };
        }

        public Task<bool> EmailInUseAsync(string email, Guid excludeUserId, CancellationToken cancellationToken = default)
        {
            return _context.Users.AnyAsync(u => u.Id != excludeUserId && u.Email.ToLower() == email.ToLower(), cancellationToken);
        }

        public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default)
        {
            return _context.Users.CountAsync(u => u.DeletedAt == null && u.Role.Name == "Admin", cancellationToken);
        }

        public async Task<AdminUserDetailDto?> GetDetailAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null)
            {
                return null;
            }

            var subscriptions = await _context.UserSubscriptions
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.StartAt)
                .Select(s => new { s.Id, s.PlanId, PlanName = s.Plan.Name, s.Status, s.StartAt, s.EndAt, s.AiCallsUsed, s.Plan.AiCallLimit })
                .ToListAsync(cancellationToken);

            var recentPayments = await _context.PaymentTransactions
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.CreatedAt)
                .Take(10)
                .Select(p => new { p.Id, p.OrderCode, PlanName = p.Plan.Name, p.Amount, p.Status, p.CreatedAt })
                .ToListAsync(cancellationToken);

            var googleStatus = await _context.GoogleConnections
                .Where(c => c.UserId == userId)
                .Select(c => c.Status)
                .FirstOrDefaultAsync(cancellationToken);

            var active = subscriptions.FirstOrDefault(s => s.Status == ActiveStatus);

            return new AdminUserDetailDto
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role.Name,
                OnboardingCompleted = user.OnboardingCompleted,
                CreatedAt = UtcTime.ToApi(user.CreatedAt),
                DeletedAt = UtcTime.ToApi(user.DeletedAt),
                CurrentPlanName = active?.PlanName,
                CurrentPlanEndAt = UtcTime.ToApi(active?.EndAt),
                TimeZone = user.TimeZone,
                WorkDayStart = user.WorkDayStart,
                WorkDayEnd = user.WorkDayEnd,
                WorksOnWeekend = user.WorksOnWeekend,
                GoogleCalendarStatus = string.IsNullOrWhiteSpace(googleStatus) ? NotConnected : googleStatus,
                ProjectCount = await _context.Projects.CountAsync(p => p.UserId == userId, cancellationToken),
                TaskCount = await _context.Tasks.CountAsync(t => t.Project.UserId == userId, cancellationToken),
                PaymentCount = await _context.PaymentTransactions.CountAsync(p => p.UserId == userId, cancellationToken),
                TotalPaid = await _context.PaymentTransactions
                    .Where(p => p.UserId == userId && p.Status == "Succeeded")
                    .SumAsync(p => p.Amount, cancellationToken),
                AiCallCount = await _context.AiusageLogs.CountAsync(l => l.UserSubscription.UserId == userId, cancellationToken),
                Subscriptions = subscriptions.Select(s => new AdminUserSubscriptionDto
                {
                    Id = s.Id,
                    PlanId = s.PlanId,
                    PlanName = s.PlanName,
                    Status = s.Status,
                    StartAt = UtcTime.ToApi(s.StartAt),
                    EndAt = UtcTime.ToApi(s.EndAt),
                    AiCallsUsed = s.AiCallsUsed,
                    AiCallLimit = s.AiCallLimit
                }).ToList(),
                RecentPayments = recentPayments.Select(p => new AdminUserPaymentDto
                {
                    Id = p.Id,
                    OrderCode = p.OrderCode,
                    PlanName = p.PlanName,
                    Amount = p.Amount,
                    Status = p.Status,
                    CreatedAt = UtcTime.ToApi(p.CreatedAt)
                }).ToList()
            };
        }
    }
}
