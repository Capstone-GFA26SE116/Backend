using METANOIA.Application.Exceptions;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;

namespace METANOIA.Infrastructure.Services
{
    // Luật trạng thái của nhánh Group, viết theo mẫu StatusRules của nhánh cá nhân nhưng không có slot và phiên làm việc
    // (xem docs/GROUP_FEATURE.md mục 5). Đặt lại đúng trạng thái đang có luôn hợp lệ.
    internal static class GroupRules
    {
        private const string ToDo = GroupWorkStatuses.ToDo;
        private const string InProgress = GroupWorkStatuses.InProgress;
        private const string Done = GroupWorkStatuses.Done;
        private const string Skipped = GroupWorkStatuses.Skipped;

        private static readonly Dictionary<string, string[]> SubTaskNext = new()
        {
            [ToDo] = [InProgress, Done, Skipped],
            [InProgress] = [ToDo, Done, Skipped],
            [Done] = [ToDo, InProgress],
            [Skipped] = [ToDo]
        };

        private static readonly Dictionary<string, string[]> TaskNext = new()
        {
            [ToDo] = [InProgress, Done, Skipped],
            [InProgress] = [ToDo, Done, Skipped],
            [Done] = [ToDo, InProgress],
            [Skipped] = [ToDo, InProgress]
        };

        private static readonly Dictionary<string, string[]> MilestoneNext = new()
        {
            [GroupWorkStatuses.Planned] = [GroupWorkStatuses.InProgress],
            [GroupWorkStatuses.InProgress] = [GroupWorkStatuses.Planned, GroupWorkStatuses.Delivered],
            [GroupWorkStatuses.Delivered] = [GroupWorkStatuses.InRevision, GroupWorkStatuses.Approved],
            [GroupWorkStatuses.InRevision] = [GroupWorkStatuses.Delivered],
            [GroupWorkStatuses.Approved] = [GroupWorkStatuses.InRevision]
        };

        public static void EnsureTask(string from, string to) => Ensure("Việc", TaskNext, from, to);

        public static void EnsureSubTask(string from, string to) => Ensure("Việc con", SubTaskNext, from, to);

        public static void EnsureMilestone(string from, string to) => Ensure("Giai đoạn", MilestoneNext, from, to);

        public static bool IsClosed(string status) => status is Done or Skipped;

        public static bool IsOpenSubTask(GroupSubTask subTask) => subTask.Status is ToDo or InProgress;

        // Việc đã bắt đầu (InProgress hoặc Done) thì giai đoạn Planned của nó tự lên InProgress
        public static bool StartsMilestone(string taskStatus) => taskStatus is InProgress or Done;

        public sealed record DeriveResult(bool StatusChanged, bool BecameDone);

        // Suy trạng thái việc từ các việc con (giống TaskProgressService của nhánh cá nhân):
        //  - Skipped là trạng thái chốt do người dùng đặt, không tự đổi. Việc không có việc con thì không suy.
        //  - Đã Done mà còn việc con ToDo hoặc InProgress (thêm mới hoặc mở lại) thì mở lại.
        //  - Mọi việc con đã xử lý (Done hoặc Skipped) và ít nhất một Done thì tự Done.
        //  - Còn lại: có việc con InProgress hoặc Done thì InProgress, không thì ToDo.
        public static DeriveResult Derive(GroupTask task, IReadOnlyCollection<GroupSubTask> subTasks, DateTime nowUtc)
        {
            if (task.Status == Skipped || subTasks.Count == 0)
            {
                return new DeriveResult(false, false);
            }

            var before = task.Status;
            var open = subTasks.Any(IsOpenSubTask);
            var anyDone = subTasks.Any(s => s.Status == Done);
            var anyActive = subTasks.Any(s => s.Status is InProgress or Done);

            if (task.Status == Done)
            {
                if (open)
                {
                    task.CompletedAt = null;
                    task.Status = anyActive ? InProgress : ToDo;
                }
            }
            else if (!open && anyDone)
            {
                task.Status = Done;
                task.CompletedAt ??= nowUtc;
            }
            else
            {
                task.Status = anyActive ? InProgress : ToDo;
            }

            return new DeriveResult(task.Status != before, task.Status == Done && before != Done);
        }

        // Việc vừa bị đóng bằng tay (Done hoặc Skipped): việc con còn ToDo hoặc InProgress thành Skipped
        public static void CascadeClose(IEnumerable<GroupSubTask> subTasks)
        {
            foreach (var subTask in subTasks.Where(IsOpenSubTask))
            {
                subTask.Status = Skipped;
                subTask.CompletedAt = null;
                subTask.CompletedById = null;
            }
        }

        public static GroupActivityLog Log(
            Guid groupId, Guid actorId, string entityType, Guid? entityId, string action, string? oldValue = null, string? newValue = null)
        {
            return new GroupActivityLog
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                ActorId = actorId,
                EntityType = entityType,
                EntityId = entityId,
                Action = action,
                OldValue = Clip(oldValue),
                NewValue = Clip(newValue),
                CreatedAt = DateTime.UtcNow
            };
        }

        private static string? Clip(string? value)
        {
            return value is { Length: > 500 } ? value[..500] : value;
        }

        private static void Ensure(string what, Dictionary<string, string[]> map, string from, string to)
        {
            if (string.Equals(from, to, StringComparison.Ordinal))
            {
                return;
            }

            if (map.TryGetValue(from, out var next) && next.Contains(to))
            {
                return;
            }

            var allowed = map.TryGetValue(from, out var list) && list.Length > 0
                ? string.Join(", ", list)
                : "không còn bước nào (trạng thái đã chốt)";
            throw new UserFriendlyException($"Không thể chuyển {what} từ '{from}' sang '{to}'. Từ '{from}' chỉ chuyển được sang: {allowed}.");
        }
    }
}
