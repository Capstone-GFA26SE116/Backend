using METANOIA.Application.Exceptions;
using METANOIA.Domain.Enums;

namespace METANOIA.Infrastructure.Services
{
    // Bảng luật chuyển trạng thái của SubTask, Task, ScheduleSlot và Milestone (xem docs/STATUS_WORKFLOW.md).
    // Mọi đường ghi trạng thái (PUT tay, Start/Stop, job nền, duyệt đề xuất AI) đều hỏi bảng này, nên không có chuyển sai lọt qua.
    // Đặt lại đúng trạng thái đang có luôn hợp lệ (FE hay gửi PUT đủ trường).
    internal static class StatusRules
    {
        // ---- Task và SubTask
        public const string ToDo = "ToDo";
        public const string Scheduled = "Scheduled";
        public const string InProgress = "InProgress";
        public const string Done = "Done";
        public const string Skipped = "Skipped";

        // ---- Milestone
        public const string Planned = "Planned";
        public const string Delivered = "Delivered";
        public const string InRevision = "InRevision";
        public const string Approved = "Approved";

        private static readonly Dictionary<string, string[]> SubTaskNext = new()
        {
            [ToDo] = [Done, Skipped],
            [Done] = [ToDo],
            [Skipped] = [ToDo]
        };

        // Trạng thái "sống" của Task (ToDo, Scheduled, InProgress) do hệ thống suy ra từ slot, phiên làm việc, subtask;
        // người dùng vẫn đặt tay được. Done và Skipped là trạng thái chốt: muốn đổi phải mở lại (về trạng thái sống) trước.
        private static readonly Dictionary<string, string[]> TaskNext = new()
        {
            [ToDo] = [Scheduled, InProgress, Done, Skipped],
            [Scheduled] = [ToDo, InProgress, Done, Skipped],
            [InProgress] = [ToDo, Scheduled, Done, Skipped],
            [Done] = [ToDo, Scheduled, InProgress],
            [Skipped] = [ToDo, Scheduled, InProgress]
        };

        private static readonly Dictionary<string, string[]> MilestoneNext = new()
        {
            [Planned] = [InProgress],
            [InProgress] = [Planned, Delivered],
            [Delivered] = [InRevision, Approved],
            [InRevision] = [Delivered],
            [Approved] = [InRevision]
        };

        private static readonly Dictionary<string, string[]> SlotNext = new()
        {
            [nameof(ScheduleSlotStatus.Proposed)] = [S.Approved, S.Cancelled],
            [S.Approved] = [S.Due, S.InProgress, S.Done, S.Missed, S.Postponed, S.Skipped, S.Cancelled],
            [S.Due] = [S.InProgress, S.Missed, S.Done, S.Approved, S.Postponed, S.Skipped, S.Cancelled],
            [S.InProgress] = [S.Done, S.Due, S.Postponed, S.Overrun],
            [S.Overrun] = [S.Done, S.Due, S.Postponed],
            [S.Missed] = [S.InProgress, S.Done, S.Approved, S.Postponed, S.Skipped, S.Cancelled],
            [S.Postponed] = [S.Approved, S.InProgress, S.Done, S.Skipped, S.Cancelled],
            [S.Done] = [],
            [S.Skipped] = [],
            [S.Cancelled] = []
        };

        // Ngoại lệ do hệ thống (không cho người dùng đặt tay): slot Done vì phiên AutoTimeout, sau đó phiên bị loại bỏ thì về Missed
        private static readonly (string From, string To)[] SlotSystemOnly = [(S.Done, S.Missed)];

        // Tên ngắn cho trạng thái slot
        private static class S
        {
            public static readonly string Approved = nameof(ScheduleSlotStatus.Approved);
            public static readonly string Due = nameof(ScheduleSlotStatus.Due);
            public static readonly string InProgress = nameof(ScheduleSlotStatus.InProgress);
            public static readonly string Overrun = nameof(ScheduleSlotStatus.Overrun);
            public static readonly string Done = nameof(ScheduleSlotStatus.Done);
            public static readonly string Skipped = nameof(ScheduleSlotStatus.Skipped);
            public static readonly string Missed = nameof(ScheduleSlotStatus.Missed);
            public static readonly string Postponed = nameof(ScheduleSlotStatus.Postponed);
            public static readonly string Cancelled = nameof(ScheduleSlotStatus.Cancelled);
        }

        // Slot "sống": còn nằm trên lịch hoặc đang làm. Task có slot sống thì là Scheduled
        public static bool IsLiveSlot(string status) =>
            status is nameof(ScheduleSlotStatus.Approved) or nameof(ScheduleSlotStatus.Due)
                or nameof(ScheduleSlotStatus.InProgress) or nameof(ScheduleSlotStatus.Overrun);

        // Slot chưa diễn ra: bị hủy khi task đóng
        public static bool IsNotStartedSlot(string status) =>
            status is nameof(ScheduleSlotStatus.Proposed) or nameof(ScheduleSlotStatus.Approved)
                or nameof(ScheduleSlotStatus.Due) or nameof(ScheduleSlotStatus.Postponed);

        public static bool IsTerminalTask(string status) => status is Done or Skipped;

        public static void EnsureSubTask(string from, string to) => Ensure("Subtask", SubTaskNext, from, to);

        public static void EnsureTask(string from, string to) => Ensure("Task", TaskNext, from, to);

        public static void EnsureMilestone(string from, string to) => Ensure("Giai đoạn", MilestoneNext, from, to);

        public static void EnsureSlot(string from, string to, bool bySystem = false)
        {
            if (bySystem && SlotSystemOnly.Any(x => x.From == from && x.To == to))
            {
                return;
            }

            Ensure("Slot", SlotNext, from, to);
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
