using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Báo cáo tiến độ nhóm và "việc của tôi". Chỉ là truy vấn tổng hợp, không ghi gì.
    public class GroupProgressService : IGroupProgressService
    {
        private readonly IGroupWorkRepository _work;
        private readonly IGroupRepository _groups;
        private readonly IGroupAccessService _access;
        private readonly TimeProvider _timeProvider;

        public GroupProgressService(IGroupWorkRepository work, IGroupRepository groups, IGroupAccessService access, TimeProvider timeProvider)
        {
            _work = work;
            _groups = groups;
            _access = access;
            _timeProvider = timeProvider;
        }

        public async Task<GroupProgressDto> GetProgressAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default)
        {
            await _access.GetAccessAsync(groupId, userId, cancellationToken);
            return await BuildProgressAsync(groupId, cancellationToken);
        }

        // Admin xem tiến độ của mọi nhóm: không kiểm tra thành viên (quyền Admin đã được kiểm ở controller)
        public Task<GroupProgressDto> GetProgressForAdminAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            return BuildProgressAsync(groupId, cancellationToken);
        }

        private async Task<GroupProgressDto> BuildProgressAsync(Guid groupId, CancellationToken cancellationToken)
        {
            var tasks = await _work.GetTasksForProgressAsync(groupId, cancellationToken);
            var members = await _groups.GetMembersAsync(groupId, cancellationToken);
            var milestones = await _work.GetMilestonesAsync(groupId, cancellationToken);
            var sessions = await _work.GetFinishedFocusSessionsOfGroupAsync(groupId, cancellationToken);
            var signals = await BuildSignalsAsync(groupId, tasks, members, sessions, cancellationToken);

            var byStatus = new Dictionary<string, int>
            {
                [GroupWorkStatuses.ToDo] = tasks.Count(t => t.Status == GroupWorkStatuses.ToDo),
                [GroupWorkStatuses.InProgress] = tasks.Count(t => t.Status == GroupWorkStatuses.InProgress),
                [GroupWorkStatuses.Done] = tasks.Count(t => t.Status == GroupWorkStatuses.Done),
                [GroupWorkStatuses.Skipped] = tasks.Count(t => t.Status == GroupWorkStatuses.Skipped)
            };

            // % hoàn thành: việc Done trên tổng số việc, không tính việc Skipped
            var countable = tasks.Count - byStatus[GroupWorkStatuses.Skipped];
            var percent = countable == 0 ? 0 : Math.Round(100.0 * byStatus[GroupWorkStatuses.Done] / countable, 1);

            var allSubTasks = tasks.SelectMany(t => t.SubTasks).ToList();

            return new GroupProgressDto
            {
                GroupId = groupId,
                TotalTasks = tasks.Count,
                TotalTrackedMinutes = Math.Round(sessions.Sum(MinutesOf), 2),
                TotalEstimatedMinutes = tasks.Sum(t => t.EstimatedMinutes ?? 0),
                TasksByStatus = byStatus,
                RemainingTasks = byStatus[GroupWorkStatuses.ToDo] + byStatus[GroupWorkStatuses.InProgress],
                UnassignedTasks = tasks.Count(t => t.AssigneeId is null && !GroupRules.IsClosed(t.Status)),
                CompletedPercent = percent,
                Members = members.Select(m => new GroupMemberProgressDto
                {
                    UserId = m.UserId,
                    FullName = m.FullName,
                    Role = m.Role,
                    AssignedTasks = tasks.Count(t => t.AssigneeId == m.UserId),
                    DoneTasks = tasks.Count(t => t.AssigneeId == m.UserId && t.Status == GroupWorkStatuses.Done),
                    AssignedSubTasks = allSubTasks.Count(s => s.AssigneeId == m.UserId),
                    DoneSubTasks = allSubTasks.Count(s => s.AssigneeId == m.UserId && s.Status == GroupWorkStatuses.Done),
                    TrackedMinutes = Math.Round(sessions.Where(x => x.UserId == m.UserId).Sum(MinutesOf), 2),
                    OverdueTasks = signals.PerMember.GetValueOrDefault(m.UserId)?.Overdue ?? 0,
                    UnscheduledTasks = signals.PerMember.GetValueOrDefault(m.UserId)?.Unscheduled ?? 0,
                    AtRiskTasks = signals.PerMember.GetValueOrDefault(m.UserId)?.AtRisk ?? 0,
                    StaleTasks = signals.PerMember.GetValueOrDefault(m.UserId)?.Stale ?? 0
                }).ToList(),
                Milestones = milestones.Select(ms => new GroupMilestoneProgressDto
                {
                    MilestoneId = ms.Id,
                    Name = ms.Name,
                    Status = ms.Status,
                    DueDate = ms.DueDate,
                    TotalTasks = tasks.Count(t => t.MilestoneId == ms.Id),
                    DoneTasks = tasks.Count(t => t.MilestoneId == ms.Id && t.Status == GroupWorkStatuses.Done)
                }).ToList(),
                Signals = signals.Dto
            };
        }

        private sealed class MemberCounters
        {
            public int Overdue { get; set; }

            public int Unscheduled { get; set; }

            public int AtRisk { get; set; }

            public int Stale { get; set; }
        }

        private sealed record SignalResult(GroupSignalsDto Dto, Dictionary<Guid, MemberCounters> PerMember);

        // Tín hiệu: chỉ tính cho việc đang mở (ToDo, InProgress) đã có người được giao (việc, hoặc một việc con giao riêng đang mở).
        //  - Quá hạn: deadline đã qua.
        //  - Chưa có lịch: không có slot đang sống nào của việc hoặc các việc con.
        //  - Nguy cơ trễ: chưa quá hạn, deadline trong RiskWindowDays ngày tới, và số phút còn phải làm (ước lượng trừ giờ đã làm) lớn hơn
        //    tổng phút slot chưa diễn ra và kết thúc trước deadline. Ước lượng lấy từ việc, rỗng thì cộng các việc con; không có thì không đánh giá.
        //  - Đứng yên: lần hoạt động cuối quá StaleAfterDays ngày.
        private async Task<SignalResult> BuildSignalsAsync(
            Guid groupId,
            IReadOnlyList<GroupTask> tasks,
            IReadOnlyList<GroupMemberDto> members,
            IReadOnlyList<GroupFocusSession> finishedSessions,
            CancellationToken cancellationToken)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var liveSlots = await _work.GetLiveSlotsOfGroupAsync(groupId, cancellationToken);
            var lastActivity = await _work.GetLastActivityByTaskAsync(groupId, cancellationToken);
            var names = members.ToDictionary(m => m.UserId, m => m.FullName);

            var dto = new GroupSignalsDto();
            var perMember = new Dictionary<Guid, MemberCounters>();

            foreach (var task in tasks.Where(t => !GroupRules.IsClosed(t.Status)))
            {
                var openSubTasks = task.SubTasks.Where(GroupRules.IsOpenSubTask).ToList();
                var owners = new HashSet<Guid>();
                if (task.AssigneeId is { } taskAssignee)
                {
                    owners.Add(taskAssignee);
                }

                foreach (var subTask in openSubTasks.Where(s => s.AssigneeId is not null))
                {
                    owners.Add(subTask.AssigneeId!.Value);
                }

                if (owners.Count == 0)
                {
                    continue;
                }

                var isOverdue = task.Deadline is { } deadline && deadline < now;

                var taskSlots = liveSlots.Where(s => s.GroupTaskId == task.Id).ToList();
                var hasSchedule = taskSlots.Count > 0;
                var scheduledMinutes = taskSlots
                    .Where(s => s.EndTime > now && (task.Deadline is not { } d || s.EndTime <= d))
                    .Sum(s => s.ProposedMinutes);

                var estimated = task.EstimatedMinutes ?? (task.SubTasks.Any(s => s.EstimatedMinutes is not null) ? task.SubTasks.Sum(s => s.EstimatedMinutes ?? 0) : (int?)null);
                var tracked = finishedSessions.Where(s => s.GroupTaskId == task.Id).Sum(MinutesOf);
                var remaining = estimated is { } e ? Math.Max(0, e - tracked) : 0;
                var atRisk = !isOverdue
                    && task.Deadline is { } riskDeadline
                    && riskDeadline <= now.AddDays(GroupRules.RiskWindowDays)
                    && estimated is > 0
                    && remaining > scheduledMinutes;

                var last = lastActivity.TryGetValue(task.Id, out var at) && at > task.CreatedAt ? at : task.CreatedAt;
                var isStale = now - last > TimeSpan.FromDays(GroupRules.StaleAfterDays);

                if (isOverdue) dto.OverdueTasks++;
                if (!hasSchedule) dto.UnscheduledTasks++;
                if (atRisk) dto.AtRiskTasks++;
                if (isStale) dto.StaleTasks++;

                foreach (var owner in owners)
                {
                    if (!perMember.TryGetValue(owner, out var counters))
                    {
                        counters = new MemberCounters();
                        perMember[owner] = counters;
                    }

                    if (isOverdue) counters.Overdue++;
                    if (!hasSchedule) counters.Unscheduled++;
                    if (atRisk) counters.AtRisk++;
                    if (isStale) counters.Stale++;
                }

                if (isOverdue || !hasSchedule || atRisk || isStale)
                {
                    dto.Tasks.Add(new GroupTaskSignalDto
                    {
                        TaskId = task.Id,
                        Title = task.Title,
                        AssigneeId = task.AssigneeId,
                        AssigneeName = task.AssigneeId is { } a && names.TryGetValue(a, out var n) ? n : null,
                        Status = task.Status,
                        Deadline = UtcTime.ToApi(task.Deadline),
                        IsOverdue = isOverdue,
                        HasSchedule = hasSchedule,
                        AtRisk = atRisk,
                        IsStale = isStale,
                        ScheduledMinutes = scheduledMinutes,
                        EstimatedMinutes = estimated,
                        LastActivityAt = UtcTime.ToApi(last)
                    });
                }
            }

            dto.Tasks = dto.Tasks.OrderByDescending(t => t.IsOverdue).ThenByDescending(t => t.AtRisk)
                .ThenBy(t => t.Deadline ?? DateTimeOffset.MaxValue).ToList();
            return new SignalResult(dto, perMember);
        }

        private static double MinutesOf(METANOIA.Domain.Entities.GroupFocusSession session)
        {
            return session.EndedAt is { } end ? Math.Max(0, (end - session.StartedAt).TotalMinutes) : 0;
        }

        public async Task<MyGroupWorkDto> GetMyWorkAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var tasks = await _work.GetTasksAssignedToAsync(userId, cancellationToken);
            var subTasks = await _work.GetSubTasksAssignedToAsync(userId, cancellationToken);

            return new MyGroupWorkDto
            {
                Tasks = tasks.Select(t => new MyGroupTaskDto
                {
                    TaskId = t.Id,
                    GroupId = t.GroupId,
                    GroupName = t.Group.Name,
                    Title = t.Title,
                    Status = t.Status,
                    Priority = t.Priority,
                    Deadline = UtcTime.ToApi(t.Deadline),
                    FixedStart = UtcTime.ToApi(t.FixedStart),
                    FixedEnd = UtcTime.ToApi(t.FixedEnd)
                }).ToList(),
                SubTasks = subTasks.Select(s => new MyGroupSubTaskDto
                {
                    SubTaskId = s.Id,
                    TaskId = s.GroupTaskId,
                    GroupId = s.GroupTask.GroupId,
                    GroupName = s.GroupTask.Group.Name,
                    TaskTitle = s.GroupTask.Title,
                    Title = s.Title,
                    Status = s.Status
                }).ToList()
            };
        }
    }
}
