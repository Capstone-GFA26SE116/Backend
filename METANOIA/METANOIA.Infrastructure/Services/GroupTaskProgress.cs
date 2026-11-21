using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Suy lại status việc từ việc con và đẩy giai đoạn Planned lên InProgress. Dùng chung cho đổi status việc con (GroupTaskService)
    // và bấm giờ Start/Stop (GroupFocusService), nên mọi đường ghi status đều đi qua cùng một luật (GroupRules).
    public sealed class GroupTaskProgress
    {
        private readonly IGroupWorkRepository _work;

        public GroupTaskProgress(IGroupWorkRepository work)
        {
            _work = work;
        }

        // Trả true nếu status của việc đã đổi
        public async Task<bool> RefreshTaskAsync(
            GroupTask task, IReadOnlyCollection<GroupSubTask> subTasks, Guid actorId, CancellationToken cancellationToken)
        {
            var oldStatus = task.Status;
            var result = GroupRules.Derive(task, subTasks, DateTime.UtcNow);
            if (result.StatusChanged)
            {
                await _work.AddActivityAsync(GroupRules.Log(task.GroupId, actorId, GroupEntityTypes.Task, task.Id, "StatusChanged", oldStatus, task.Status), cancellationToken);
                await StartMilestoneIfNeededAsync(task, actorId, cancellationToken);
            }

            return result.StatusChanged;
        }

        // Giai đoạn Planned tự lên InProgress khi có việc bắt đầu làm hoặc đã xong. Không tự đổi các bước sau.
        public async Task StartMilestoneIfNeededAsync(GroupTask task, Guid actorId, CancellationToken cancellationToken)
        {
            if (task.MilestoneId is not { } milestoneId || !GroupRules.StartsMilestone(task.Status))
            {
                return;
            }

            var milestone = await _work.GetMilestoneAsync(milestoneId, cancellationToken);
            if (milestone is not null && milestone.Status == GroupWorkStatuses.Planned)
            {
                milestone.Status = GroupWorkStatuses.InProgress;
                await _work.AddActivityAsync(GroupRules.Log(task.GroupId, actorId, GroupEntityTypes.Milestone, milestone.Id,
                    "StatusChanged", GroupWorkStatuses.Planned, GroupWorkStatuses.InProgress), cancellationToken);
            }
        }
    }
}
