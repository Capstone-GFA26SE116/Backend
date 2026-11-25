using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Kiểm tra lịch của người nhận việc (docs/GROUP_FEATURE.md mục 11). Chỉ ĐỌC dữ liệu của nhánh cá nhân
    // (slot đang hoạt động, event Google đã lưu) và trả bận hoặc rảnh: không lộ tên dự án hay tiêu đề lịch.
    //  - Việc có giờ cố định: so với slot cá nhân đang hoạt động, event Google (không tính cả ngày) và việc nhóm khác có giờ cố định.
    //  - Việc có deadline và ước lượng: cảnh báo khối lượng nếu tổng phút ước lượng của tuần (7 ngày kết thúc ở deadline) vượt
    //    số phút làm việc của một tuần theo cài đặt giờ làm của người nhận.
    public class GroupAvailabilityService : IGroupAvailabilityService
    {
        private const int WorkloadWindowDays = 7;
        private const int DefaultDayMinutes = 8 * 60;

        private readonly IGroupWorkRepository _work;
        private readonly IGroupRepository _groups;
        private readonly ICalendarRepository _calendar;
        private readonly IGroupAccessService _access;

        public GroupAvailabilityService(
            IGroupWorkRepository work,
            IGroupRepository groups,
            ICalendarRepository calendar,
            IGroupAccessService access)
        {
            _work = work;
            _groups = groups;
            _calendar = calendar;
            _access = access;
        }

        public async Task<GroupAssigneeAvailabilityDto> CheckAsync(
            Guid assigneeId,
            DateTime? fixedStartUtc,
            DateTime? fixedEndUtc,
            DateTime? deadlineUtc,
            int? estimatedMinutes,
            Guid? excludeTaskId,
            CancellationToken cancellationToken = default)
        {
            var result = new GroupAssigneeAvailabilityDto { AssigneeId = assigneeId };

            if (fixedStartUtc is { } start && fixedEndUtc is { } end)
            {
                var slots = await _calendar.GetSlotsAsync(assigneeId, start, end, false, cancellationToken);
                // Slot nhóm của chính việc đang kiểm tra không tính là bận (người này đang xếp lịch cho đúng việc đó)
                foreach (var slot in slots.Where(s => StatusRules.IsLiveSlot(s.Status) && s.StartTime < end && s.EndTime > start
                                                      && (s.GroupTaskId is null || s.GroupTaskId != excludeTaskId)))
                {
                    result.Conflicts.Add(Period(slot.StartTime, slot.EndTime, slot.GroupTaskId is null ? "PersonalSchedule" : "GroupSchedule"));
                }

                var events = await _calendar.GetGoogleEventsAsync(assigneeId, start, end, cancellationToken);
                foreach (var googleEvent in events.Where(e => !e.IsAllDay && !e.IsCancelled))
                {
                    result.Conflicts.Add(Period(googleEvent.StartTime, googleEvent.EndTime, "GoogleCalendar"));
                }

                var tasks = await _work.GetFixedTasksOverlappingAsync(assigneeId, start, end, excludeTaskId, cancellationToken);
                foreach (var task in tasks)
                {
                    result.Conflicts.Add(Period(task.FixedStart!.Value, task.FixedEnd!.Value, "GroupTask"));
                }

                result.Conflicts = result.Conflicts.OrderBy(c => c.Start).ToList();
            }

            result.IsBusy = result.Conflicts.Count > 0;

            if (deadlineUtc is { } deadline && estimatedMinutes is { } estimate)
            {
                var others = await _work.SumOpenEstimatedMinutesAsync(
                    assigneeId, deadline.AddDays(-WorkloadWindowDays), deadline, excludeTaskId, cancellationToken);

                var hours = await _calendar.GetWorkingHoursAsync(assigneeId, cancellationToken);
                var dayMinutes = hours is null ? DefaultDayMinutes : (int)(hours.WorkDayEnd - hours.WorkDayStart).TotalMinutes;
                if (dayMinutes <= 0)
                {
                    dayMinutes = DefaultDayMinutes;
                }

                var days = hours is { WorksOnWeekend: true } ? 7 : 5;
                var capacity = dayMinutes * days;
                var assigned = others + estimate;
                result.Workload = new GroupWorkloadDto
                {
                    AssignedMinutes = assigned,
                    CapacityMinutes = capacity,
                    IsOverloaded = assigned > capacity
                };
            }

            result.NeedsConfirmation = result.IsBusy || result.Workload is { IsOverloaded: true };
            return result;
        }

        public async Task<GroupAssigneeAvailabilityDto> CheckForTaskAsync(Guid userId, Guid taskId, Guid assigneeId, CancellationToken cancellationToken = default)
        {
            var task = await _work.GetTaskAsync(taskId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy việc.");
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireLeader();

            var member = await _groups.GetMemberAsync(task.GroupId, assigneeId, cancellationToken);
            if (member is null)
            {
                throw new UserFriendlyException("Chỉ kiểm tra được lịch của thành viên trong nhóm.");
            }

            return await CheckAsync(assigneeId, task.FixedStart, task.FixedEnd, task.Deadline, task.EstimatedMinutes, task.Id, cancellationToken);
        }

        public async Task EnsureAvailableAsync(
            Guid assigneeId,
            DateTime? fixedStartUtc,
            DateTime? fixedEndUtc,
            DateTime? deadlineUtc,
            int? estimatedMinutes,
            Guid? excludeTaskId,
            bool confirm,
            CancellationToken cancellationToken = default)
        {
            if (confirm)
            {
                return;
            }

            var check = await CheckAsync(assigneeId, fixedStartUtc, fixedEndUtc, deadlineUtc, estimatedMinutes, excludeTaskId, cancellationToken);
            if (!check.NeedsConfirmation)
            {
                return;
            }

            var reasons = new List<string>();
            if (check.IsBusy)
            {
                reasons.Add($"đang bận vào giờ này ({check.Conflicts.Count} khoảng trùng)");
            }

            if (check.Workload is { IsOverloaded: true } workload)
            {
                reasons.Add($"khối lượng việc trong tuần của deadline vượt giờ làm việc ({workload.AssignedMinutes}/{workload.CapacityMinutes} phút)");
            }

            throw new GroupAssigneeBusyException(
                "Người được giao " + string.Join(" và ", reasons) + ". Gửi lại kèm confirmConflicts = true để vẫn giao.",
                new { assigneeId, isBusy = check.IsBusy, conflicts = check.Conflicts, workload = check.Workload });
        }

        private static GroupBusyPeriodDto Period(DateTime start, DateTime end, string source)
        {
            return new GroupBusyPeriodDto { Start = UtcTime.ToApi(start), End = UtcTime.ToApi(end), Source = source };
        }
    }
}
