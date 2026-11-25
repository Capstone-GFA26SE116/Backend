using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class GroupWorkRepository : IGroupWorkRepository
    {
        private readonly AppDbContext _context;

        public GroupWorkRepository(AppDbContext context)
        {
            _context = context;
        }

        // ---------------------------------------------------------------- Giai đoạn
        public async Task<IReadOnlyList<GroupMilestone>> GetMilestonesAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            return await _context.GroupMilestones
                .Where(m => m.GroupId == groupId)
                .OrderBy(m => m.OrderIndex)
                .ToListAsync(cancellationToken);
        }

        public Task<GroupMilestone?> GetMilestoneAsync(Guid milestoneId, CancellationToken cancellationToken = default)
        {
            return _context.GroupMilestones.FirstOrDefaultAsync(m => m.Id == milestoneId, cancellationToken);
        }

        public Task<bool> MilestoneOrderIndexTakenAsync(Guid groupId, int orderIndex, Guid excludeId, CancellationToken cancellationToken = default)
        {
            return _context.GroupMilestones.AnyAsync(m => m.GroupId == groupId && m.OrderIndex == orderIndex && m.Id != excludeId, cancellationToken);
        }

        public Task<bool> MilestoneBelongsToGroupAsync(Guid milestoneId, Guid groupId, CancellationToken cancellationToken = default)
        {
            return _context.GroupMilestones.AnyAsync(m => m.Id == milestoneId && m.GroupId == groupId, cancellationToken);
        }

        public async Task<IReadOnlyList<GroupOpenTaskDto>> GetOpenTasksAsync(Guid milestoneId, CancellationToken cancellationToken = default)
        {
            return await _context.GroupTasks
                .Where(t => t.MilestoneId == milestoneId
                            && t.Status != GroupWorkStatuses.Done && t.Status != GroupWorkStatuses.Skipped)
                .OrderBy(t => t.CreatedAt)
                .Select(t => new GroupOpenTaskDto
                {
                    Id = t.Id,
                    Title = t.Title,
                    Status = t.Status,
                    OpenSubTaskCount = t.SubTasks.Count(s => s.Status == GroupWorkStatuses.ToDo || s.Status == GroupWorkStatuses.InProgress)
                })
                .ToListAsync(cancellationToken);
        }

        public async Task AddMilestoneAsync(GroupMilestone milestone, CancellationToken cancellationToken = default)
        {
            await _context.GroupMilestones.AddAsync(milestone, cancellationToken);
        }

        public void RemoveMilestone(GroupMilestone milestone)
        {
            _context.GroupMilestones.Remove(milestone);
        }

        // ---------------------------------------------------------------- Việc
        public async Task<IReadOnlyList<GroupTask>> GetTasksAsync(Guid groupId, string? status, Guid? assigneeId, Guid? milestoneId, CancellationToken cancellationToken = default)
        {
            var query = _context.GroupTasks
                .Include(t => t.SubTasks)
                .Include(t => t.Assignee)
                .Where(t => t.GroupId == groupId);

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(t => t.Status == status);
            }

            if (assigneeId is { } assignee)
            {
                query = query.Where(t => t.AssigneeId == assignee);
            }

            if (milestoneId is { } milestone)
            {
                query = query.Where(t => t.MilestoneId == milestone);
            }

            return await query.OrderBy(t => t.CreatedAt).ToListAsync(cancellationToken);
        }

        public Task<GroupTask?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            return _context.GroupTasks
                .Include(t => t.SubTasks)
                .Include(t => t.Assignee)
                .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        }

        public async Task AddTaskAsync(GroupTask task, CancellationToken cancellationToken = default)
        {
            await _context.GroupTasks.AddAsync(task, cancellationToken);
        }

        public void RemoveTask(GroupTask task)
        {
            _context.GroupTasks.Remove(task);
        }

        // ---------------------------------------------------------------- Việc con
        public Task<GroupSubTask?> GetSubTaskAsync(Guid subTaskId, CancellationToken cancellationToken = default)
        {
            return _context.GroupSubTasks
                .Include(s => s.GroupTask).ThenInclude(t => t.SubTasks)
                .Include(s => s.Assignee)
                .FirstOrDefaultAsync(s => s.Id == subTaskId, cancellationToken);
        }

        public Task<bool> SubTaskOrderIndexTakenAsync(Guid taskId, int orderIndex, Guid excludeId, CancellationToken cancellationToken = default)
        {
            return _context.GroupSubTasks.AnyAsync(s => s.GroupTaskId == taskId && s.OrderIndex == orderIndex && s.Id != excludeId, cancellationToken);
        }

        public async Task<int> NextSubTaskOrderIndexAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            var max = await _context.GroupSubTasks
                .Where(s => s.GroupTaskId == taskId)
                .Select(s => (int?)s.OrderIndex)
                .MaxAsync(cancellationToken);
            return (max ?? 0) + 1;
        }

        public async Task AddSubTaskAsync(GroupSubTask subTask, CancellationToken cancellationToken = default)
        {
            await _context.GroupSubTasks.AddAsync(subTask, cancellationToken);
        }

        public void RemoveSubTask(GroupSubTask subTask)
        {
            _context.GroupSubTasks.Remove(subTask);
        }

        // ---------------------------------------------------------------- Bình luận
        public async Task<IReadOnlyList<GroupTaskComment>> GetCommentsAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            return await _context.GroupTaskComments
                .Include(c => c.Author)
                .Where(c => c.GroupTaskId == taskId)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public Task<GroupTaskComment?> GetCommentAsync(Guid commentId, CancellationToken cancellationToken = default)
        {
            return _context.GroupTaskComments
                .Include(c => c.GroupTask)
                .Include(c => c.Author)
                .FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken);
        }

        public async Task AddCommentAsync(GroupTaskComment comment, CancellationToken cancellationToken = default)
        {
            await _context.GroupTaskComments.AddAsync(comment, cancellationToken);
        }

        public void RemoveComment(GroupTaskComment comment)
        {
            _context.GroupTaskComments.Remove(comment);
        }

        // ---------------------------------------------------------------- Nhật ký
        public async Task AddActivityAsync(GroupActivityLog log, CancellationToken cancellationToken = default)
        {
            await _context.GroupActivityLogs.AddAsync(log, cancellationToken);
        }

        public async Task<IReadOnlyList<GroupActivityLog>> GetActivityAsync(Guid groupId, int skip, int take, CancellationToken cancellationToken = default)
        {
            return await _context.GroupActivityLogs
                .Include(l => l.Actor)
                .Where(l => l.GroupId == groupId)
                .OrderByDescending(l => l.CreatedAt)
                .Skip(skip)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        // ---------------------------------------------------------------- Người dùng
        public async Task<Dictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<Guid, string>();
            }

            return await _context.Users
                .Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        }

        // ---------------------------------------------------------------- Tiến độ và việc của tôi
        public async Task<IReadOnlyList<GroupTask>> GetTasksForProgressAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            return await _context.GroupTasks
                .AsNoTracking()
                .Include(t => t.SubTasks)
                .Where(t => t.GroupId == groupId)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<GroupTask>> GetTasksAssignedToAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.GroupTasks
                .AsNoTracking()
                .Include(t => t.Group)
                .Where(t => t.AssigneeId == userId && t.Group.Status == GroupStatuses.Active)
                .OrderBy(t => t.Deadline == null).ThenBy(t => t.Deadline).ThenBy(t => t.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<GroupSubTask>> GetSubTasksAssignedToAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.GroupSubTasks
                .AsNoTracking()
                .Include(s => s.GroupTask).ThenInclude(t => t.Group)
                .Where(s => s.AssigneeId == userId && s.GroupTask.Group.Status == GroupStatuses.Active)
                .OrderBy(s => s.GroupTask.CreatedAt).ThenBy(s => s.OrderIndex)
                .ToListAsync(cancellationToken);
        }

        // ---------------------------------------------------------------- Phiên bấm giờ (đợt 2)
        public async Task AddFocusSessionAsync(GroupFocusSession session, CancellationToken cancellationToken = default)
        {
            await _context.GroupFocusSessions.AddAsync(session, cancellationToken);
        }

        public Task<GroupFocusSession?> GetFocusSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            return _context.GroupFocusSessions
                .Include(s => s.GroupTask).ThenInclude(t => t.SubTasks)
                .Include(s => s.GroupSubTask)
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        }

        public Task<GroupFocusSession?> GetRunningFocusSessionAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.GroupFocusSessions
                .Include(s => s.GroupTask).ThenInclude(t => t.Group)
                .Include(s => s.GroupSubTask)
                .FirstOrDefaultAsync(s => s.UserId == userId && s.EndedAt == null, cancellationToken);
        }

        public async Task<IReadOnlyList<GroupFocusSession>> GetRunningFocusSessionsOfTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            return await _context.GroupFocusSessions
                .Where(s => s.GroupTaskId == taskId && s.EndedAt == null)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<GroupFocusSession>> GetFocusSessionsOfTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            return await _context.GroupFocusSessions
                .AsNoTracking()
                .Include(s => s.User)
                .Include(s => s.GroupSubTask)
                .Where(s => s.GroupTaskId == taskId)
                .OrderBy(s => s.StartedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<GroupFocusSession>> GetFinishedFocusSessionsOfGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            return await _context.GroupFocusSessions
                .AsNoTracking()
                .Where(s => s.GroupTask.GroupId == groupId && s.EndedAt != null)
                .ToListAsync(cancellationToken);
        }

        // ---------------------------------------------------------------- Tín hiệu tiến độ
        private static readonly string[] LiveSlotStatuses = { "Approved", "Due", "InProgress", "Overrun" };

        public async Task<IReadOnlyList<ScheduleSlot>> GetLiveSlotsOfGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            return await _context.ScheduleSlots
                .AsNoTracking()
                .Where(s => s.GroupTask != null && s.GroupTask.GroupId == groupId && LiveSlotStatuses.Contains(s.Status))
                .ToListAsync(cancellationToken);
        }

        public async Task<Dictionary<Guid, DateTime>> GetLastActivityByTaskAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<Guid, DateTime>();

            void Add(Guid taskId, DateTime at)
            {
                if (!result.TryGetValue(taskId, out var current) || at > current)
                {
                    result[taskId] = at;
                }
            }

            // Nhật ký của việc (EntityId là id việc) và của việc con (EntityId là id việc con, đổi sang việc cha)
            var taskLogs = await _context.GroupActivityLogs.AsNoTracking()
                .Where(l => l.GroupId == groupId && l.EntityType == GroupEntityTypes.Task && l.EntityId != null)
                .GroupBy(l => l.EntityId!.Value)
                .Select(g => new { Id = g.Key, At = g.Max(l => l.CreatedAt) })
                .ToListAsync(cancellationToken);
            foreach (var row in taskLogs)
            {
                Add(row.Id, row.At);
            }

            var subTaskToTask = await _context.GroupSubTasks.AsNoTracking()
                .Where(s => s.GroupTask.GroupId == groupId)
                .Select(s => new { s.Id, s.GroupTaskId })
                .ToDictionaryAsync(s => s.Id, s => s.GroupTaskId, cancellationToken);
            var subTaskLogs = await _context.GroupActivityLogs.AsNoTracking()
                .Where(l => l.GroupId == groupId && l.EntityType == GroupEntityTypes.SubTask && l.EntityId != null)
                .GroupBy(l => l.EntityId!.Value)
                .Select(g => new { Id = g.Key, At = g.Max(l => l.CreatedAt) })
                .ToListAsync(cancellationToken);
            foreach (var row in subTaskLogs)
            {
                if (subTaskToTask.TryGetValue(row.Id, out var taskId))
                {
                    Add(taskId, row.At);
                }
            }

            var comments = await _context.GroupTaskComments.AsNoTracking()
                .Where(c => c.GroupTask.GroupId == groupId)
                .GroupBy(c => c.GroupTaskId)
                .Select(g => new { Id = g.Key, At = g.Max(c => c.CreatedAt) })
                .ToListAsync(cancellationToken);
            foreach (var row in comments)
            {
                Add(row.Id, row.At);
            }

            var sessions = await _context.GroupFocusSessions.AsNoTracking()
                .Where(s => s.GroupTask.GroupId == groupId)
                .GroupBy(s => s.GroupTaskId)
                .Select(g => new { Id = g.Key, At = g.Max(s => s.EndedAt ?? s.StartedAt) })
                .ToListAsync(cancellationToken);
            foreach (var row in sessions)
            {
                Add(row.Id, row.At);
            }

            return result;
        }

        // ---------------------------------------------------------------- Đề xuất đổi giờ (đợt 2)
        public async Task AddChangeRequestAsync(GroupTaskChangeRequest request, CancellationToken cancellationToken = default)
        {
            await _context.GroupTaskChangeRequests.AddAsync(request, cancellationToken);
        }

        public Task<GroupTaskChangeRequest?> GetChangeRequestAsync(Guid requestId, CancellationToken cancellationToken = default)
        {
            return _context.GroupTaskChangeRequests
                .Include(r => r.GroupTask).ThenInclude(t => t.SubTasks)
                .Include(r => r.GroupTask).ThenInclude(t => t.Assignee)
                .Include(r => r.RequestedBy)
                .Include(r => r.DecidedBy)
                .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        }

        public async Task<IReadOnlyList<GroupTaskChangeRequest>> GetChangeRequestsOfGroupAsync(
            Guid groupId, string? status, Guid? requestedById, CancellationToken cancellationToken = default)
        {
            var query = _context.GroupTaskChangeRequests
                .AsNoTracking()
                .Include(r => r.GroupTask)
                .Include(r => r.RequestedBy)
                .Include(r => r.DecidedBy)
                .Where(r => r.GroupTask.GroupId == groupId);

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(r => r.Status == status);
            }

            if (requestedById is { } requester)
            {
                query = query.Where(r => r.RequestedById == requester);
            }

            return await query.OrderByDescending(r => r.CreatedAt).ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<GroupTaskChangeRequest>> GetChangeRequestsOfTaskAsync(
            Guid taskId, Guid? requestedById, CancellationToken cancellationToken = default)
        {
            var query = _context.GroupTaskChangeRequests
                .AsNoTracking()
                .Include(r => r.GroupTask)
                .Include(r => r.RequestedBy)
                .Include(r => r.DecidedBy)
                .Where(r => r.GroupTaskId == taskId);

            if (requestedById is { } requester)
            {
                query = query.Where(r => r.RequestedById == requester);
            }

            return await query.OrderByDescending(r => r.CreatedAt).ToListAsync(cancellationToken);
        }

        public Task<GroupTaskChangeRequest?> GetPendingChangeRequestAsync(Guid taskId, Guid requestedById, CancellationToken cancellationToken = default)
        {
            return _context.GroupTaskChangeRequests.FirstOrDefaultAsync(
                r => r.GroupTaskId == taskId && r.RequestedById == requestedById && r.Status == GroupChangeRequestStatuses.Pending,
                cancellationToken);
        }

        // ---------------------------------------------------------------- Kiểm tra lịch người nhận việc (đợt 2)
        public async Task<IReadOnlyList<GroupTask>> GetFixedTasksOverlappingAsync(
            Guid userId, DateTime startUtc, DateTime endUtc, Guid? excludeTaskId, CancellationToken cancellationToken = default)
        {
            return await _context.GroupTasks
                .AsNoTracking()
                .Where(t => t.AssigneeId == userId
                            && t.FixedStart != null && t.FixedEnd != null
                            && t.FixedStart < endUtc && t.FixedEnd > startUtc
                            && t.Status != GroupWorkStatuses.Done && t.Status != GroupWorkStatuses.Skipped
                            && t.Group.Status == GroupStatuses.Active
                            && (excludeTaskId == null || t.Id != excludeTaskId))
                .ToListAsync(cancellationToken);
        }

        public async Task<int> SumOpenEstimatedMinutesAsync(
            Guid userId, DateTime fromUtc, DateTime toUtc, Guid? excludeTaskId, CancellationToken cancellationToken = default)
        {
            var sum = await _context.GroupTasks
                .Where(t => t.AssigneeId == userId
                            && t.EstimatedMinutes != null
                            && t.Deadline != null && t.Deadline >= fromUtc && t.Deadline <= toUtc
                            && t.Status != GroupWorkStatuses.Done && t.Status != GroupWorkStatuses.Skipped
                            && t.Group.Status == GroupStatuses.Active
                            && (excludeTaskId == null || t.Id != excludeTaskId))
                .SumAsync(t => (int?)t.EstimatedMinutes, cancellationToken);
            return sum ?? 0;
        }
    }
}
