using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class AdminAuditLogRepository : IAdminAuditLogRepository
    {
        private readonly AppDbContext _context;

        public AdminAuditLogRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<(IReadOnlyList<(AdminActionLog Log, string AdminEmail)> Items, int TotalCount)> SearchAsync(
            Guid? adminId, string? targetType, Guid? targetId, string? action,
            DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var query = _context.AdminActionLogs.AsQueryable();

            if (adminId is { } admin) query = query.Where(l => l.AdminId == admin);
            if (!string.IsNullOrWhiteSpace(targetType)) query = query.Where(l => l.TargetType == targetType);
            if (targetId is { } target) query = query.Where(l => l.TargetId == target);
            if (!string.IsNullOrWhiteSpace(action)) query = query.Where(l => l.Action == action);
            if (from is { } start) query = query.Where(l => l.CreatedAt >= start);
            if (to is { } end) query = query.Where(l => l.CreatedAt < end);

            var total = await query.CountAsync(cancellationToken);

            var rows = await query
                .OrderByDescending(l => l.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(l => new { Log = l, AdminEmail = l.Admin.Email })
                .ToListAsync(cancellationToken);

            return (rows.Select(r => (r.Log, r.AdminEmail)).ToList(), total);
        }
    }
}
