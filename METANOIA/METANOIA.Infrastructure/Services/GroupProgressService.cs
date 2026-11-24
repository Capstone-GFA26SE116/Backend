using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
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

        public GroupProgressService(IGroupWorkRepository work, IGroupRepository groups, IGroupAccessService access)
        {
            _work = work;
            _groups = groups;
            _access = access;
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
                    TrackedMinutes = Math.Round(sessions.Where(x => x.UserId == m.UserId).Sum(MinutesOf), 2)
                }).ToList(),
                Milestones = milestones.Select(ms => new GroupMilestoneProgressDto
                {
                    MilestoneId = ms.Id,
                    Name = ms.Name,
                    Status = ms.Status,
                    DueDate = ms.DueDate,
                    TotalTasks = tasks.Count(t => t.MilestoneId == ms.Id),
                    DoneTasks = tasks.Count(t => t.MilestoneId == ms.Id && t.Status == GroupWorkStatuses.Done)
                }).ToList()
            };
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
