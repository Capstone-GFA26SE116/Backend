using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class AdminGroupRepository : IAdminGroupRepository
    {
        private readonly AppDbContext _context;

        public AdminGroupRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<(IReadOnlyList<AdminGroupListItemDto> Items, int TotalCount)> SearchAsync(
            string? search, string? status, Guid? ownerId, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var query = _context.WorkGroups.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(g => g.Status == status);
            }

            if (ownerId is { } owner)
            {
                query = query.Where(g => g.OwnerId == owner);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(g => g.Name.ToLower().Contains(term)
                    || g.Owner.Email.ToLower().Contains(term)
                    || g.Owner.FullName.ToLower().Contains(term));
            }

            var total = await query.CountAsync(cancellationToken);

            var items = await query
                .OrderByDescending(g => g.CreatedAt)
                .ThenBy(g => g.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(g => new AdminGroupListItemDto
                {
                    Id = g.Id,
                    Name = g.Name,
                    Status = g.Status,
                    OwnerId = g.OwnerId,
                    OwnerName = g.Owner.FullName,
                    OwnerEmail = g.Owner.Email,
                    MemberCount = g.Members.Count,
                    TaskCount = g.Tasks.Count,
                    StartDate = g.StartDate,
                    Deadline = g.Deadline,
                    CreatedAt = g.CreatedAt
                })
                .ToListAsync(cancellationToken);

            return (items, total);
        }

        public Task<WorkGroup?> GetGroupWithOwnerAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            return _context.WorkGroups.AsNoTracking().Include(g => g.Owner).FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        }

        public async Task<AdminGroupStatsDto> GetStatsAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var since = nowUtc.AddDays(-30);

            var statusCounts = await _context.WorkGroups
                .GroupBy(g => g.Status)
                .Select(x => new { Status = x.Key, Count = x.Count() })
                .ToListAsync(cancellationToken);

            var taskCounts = await _context.GroupTasks
                .GroupBy(t => t.Status)
                .Select(x => new { Status = x.Key, Count = x.Count() })
                .ToListAsync(cancellationToken);

            int Tasks(string status) => taskCounts.Where(x => x.Status == status).Sum(x => x.Count);

            return new AdminGroupStatsDto
            {
                TotalGroups = statusCounts.Sum(x => x.Count),
                ActiveGroups = statusCounts.Where(x => x.Status == GroupStatuses.Active).Sum(x => x.Count),
                ArchivedGroups = statusCounts.Where(x => x.Status == GroupStatuses.Archived).Sum(x => x.Count),
                TotalMemberships = await _context.GroupMembers.CountAsync(cancellationToken),
                DistinctMembers = await _context.GroupMembers.Select(m => m.UserId).Distinct().CountAsync(cancellationToken),
                TotalTasks = taskCounts.Sum(x => x.Count),
                TasksByStatus = new Dictionary<string, int>
                {
                    [GroupWorkStatuses.ToDo] = Tasks(GroupWorkStatuses.ToDo),
                    [GroupWorkStatuses.InProgress] = Tasks(GroupWorkStatuses.InProgress),
                    [GroupWorkStatuses.Done] = Tasks(GroupWorkStatuses.Done),
                    [GroupWorkStatuses.Skipped] = Tasks(GroupWorkStatuses.Skipped)
                },
                GroupsCreatedLast30Days = await _context.WorkGroups.CountAsync(g => g.CreatedAt >= since, cancellationToken),
                PendingInvitations = await _context.GroupInvitations.CountAsync(
                    i => i.Status == GroupInvitationStatuses.Pending && i.ExpiresAt > nowUtc, cancellationToken),
                RunningFocusSessions = await _context.GroupFocusSessions.CountAsync(s => s.EndedAt == null, cancellationToken)
            };
        }
    }
}
