using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class GroupRepository : IGroupRepository
    {
        private readonly AppDbContext _context;

        public GroupRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<WorkGroup?> GetGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            return _context.WorkGroups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        }

        public async Task<IReadOnlyList<GroupSummaryRow>> GetGroupsOfUserAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken = default)
        {
            var query = _context.GroupMembers.Where(m => m.UserId == userId);
            if (!includeArchived)
            {
                query = query.Where(m => m.Group.Status == GroupStatuses.Active);
            }

            var rows = await query
                .OrderBy(m => m.Group.Name)
                .Select(m => new { m.Group, m.Role, Count = m.Group.Members.Count })
                .ToListAsync(cancellationToken);

            return rows.Select(r => new GroupSummaryRow(r.Group, r.Role, r.Count)).ToList();
        }

        public async Task AddGroupAsync(WorkGroup group, CancellationToken cancellationToken = default)
        {
            await _context.WorkGroups.AddAsync(group, cancellationToken);
        }

        public Task<GroupMember?> GetMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, cancellationToken);
        }

        public async Task<IReadOnlyList<GroupMemberDto>> GetMembersAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            return await _context.GroupMembers
                .Where(m => m.GroupId == groupId)
                .OrderBy(m => m.JoinedAt)
                .Select(m => new GroupMemberDto
                {
                    UserId = m.UserId,
                    FullName = m.User.FullName,
                    Email = m.User.Email,
                    Role = m.Role,
                    JoinedAt = m.JoinedAt
                })
                .ToListAsync(cancellationToken);
        }

        public Task<int> CountMembersAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            return _context.GroupMembers.CountAsync(m => m.GroupId == groupId, cancellationToken);
        }

        public Task<int> CountLeadersAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            return _context.GroupMembers.CountAsync(m => m.GroupId == groupId && m.Role == GroupRoles.Leader, cancellationToken);
        }

        public async Task<IReadOnlyList<Guid>> GetLeaderIdsAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            return await _context.GroupMembers
                .Where(m => m.GroupId == groupId && m.Role == GroupRoles.Leader)
                .Select(m => m.UserId)
                .ToListAsync(cancellationToken);
        }

        public async Task AddMemberAsync(GroupMember member, CancellationToken cancellationToken = default)
        {
            await _context.GroupMembers.AddAsync(member, cancellationToken);
        }

        public void RemoveMember(GroupMember member)
        {
            _context.GroupMembers.Remove(member);
        }

        public async Task<int> ClearAssignmentsAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
        {
            var tasks = await _context.GroupTasks
                .Where(t => t.GroupId == groupId && t.AssigneeId == userId)
                .ToListAsync(cancellationToken);
            foreach (var task in tasks)
            {
                task.AssigneeId = null;
            }

            var subTasks = await _context.GroupSubTasks
                .Where(s => s.GroupTask.GroupId == groupId && s.AssigneeId == userId)
                .ToListAsync(cancellationToken);
            foreach (var subTask in subTasks)
            {
                subTask.AssigneeId = null;
            }

            return tasks.Count + subTasks.Count;
        }

        public async Task AddInvitationAsync(GroupInvitation invitation, CancellationToken cancellationToken = default)
        {
            await _context.GroupInvitations.AddAsync(invitation, cancellationToken);
        }

        public Task<GroupInvitation?> GetInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default)
        {
            return _context.GroupInvitations
                .Include(i => i.Group)
                .Include(i => i.InvitedBy)
                .FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
        }

        public Task<GroupInvitation?> GetInvitationByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            return _context.GroupInvitations
                .Include(i => i.Group)
                .Include(i => i.InvitedBy)
                .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);
        }

        public async Task<IReadOnlyList<GroupInvitation>> GetInvitationsOfGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            return await _context.GroupInvitations
                .Include(i => i.Group)
                .Include(i => i.InvitedBy)
                .Where(i => i.GroupId == groupId)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<GroupInvitation>> GetPendingInvitationsForEmailAsync(string email, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            return await _context.GroupInvitations
                .Include(i => i.Group)
                .Include(i => i.InvitedBy)
                .Where(i => i.Email == email && i.Status == GroupInvitationStatuses.Pending && i.ExpiresAt > nowUtc)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public Task<GroupInvitation?> GetPendingInvitationAsync(Guid groupId, string email, CancellationToken cancellationToken = default)
        {
            return _context.GroupInvitations
                .FirstOrDefaultAsync(i => i.GroupId == groupId && i.Email == email && i.Status == GroupInvitationStatuses.Pending, cancellationToken);
        }
    }
}
