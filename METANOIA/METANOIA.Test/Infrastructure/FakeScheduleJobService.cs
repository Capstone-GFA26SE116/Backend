using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;

namespace METANOIA.Test.Infrastructure
{
    // Thay Hangfire trong test: ghi lại các lần "hẹn" và "hủy" job để kiểm tra, không hẹn job thật.
    // Muốn chạy logic của một job thì gọi thẳng SlotBackgroundJobs.<Tên job>(slotId) từ DI.
    public class FakeScheduleJobService : IScheduleJobService
    {
        private readonly object _gate = new();
        private readonly List<JobCall> _calls = new();

        public record JobCall(string Method, Guid SlotId, DateTimeOffset? At = null);

        public IReadOnlyList<JobCall> Calls
        {
            get { lock (_gate) { return _calls.ToList(); } }
        }

        public IEnumerable<JobCall> CallsFor(string method, Guid slotId) =>
            Calls.Where(c => c.Method == method && c.SlotId == slotId);

        public void ScheduleAllJobsForSlot(ScheduleSlot slot) => Add(new JobCall(nameof(ScheduleAllJobsForSlot), slot.Id));

        public void RescheduleSlotJobs(Guid slotId, DateTimeOffset actualStartTime) =>
            Add(new JobCall(nameof(RescheduleSlotJobs), slotId, actualStartTime));

        public void CancelOverrunJobs(Guid slotId) => Add(new JobCall(nameof(CancelOverrunJobs), slotId));

        public void CancelAllJobs(Guid slotId) => Add(new JobCall(nameof(CancelAllJobs), slotId));

        private void Add(JobCall call)
        {
            lock (_gate) { _calls.Add(call); }
        }
    }
}
