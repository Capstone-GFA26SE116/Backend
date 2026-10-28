using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;

namespace METANOIA.Infrastructure.Services;

// Dùng khi tắt Hangfire (Hangfire:Enabled = false): không hẹn job nào, các luồng khác vẫn chạy bình thường
public sealed class NullScheduleJobService : IScheduleJobService
{
    public void ScheduleAllJobsForSlot(ScheduleSlot slot) { }

    public void RescheduleSlotJobs(Guid slotId, DateTimeOffset actualStartTime) { }

    public void CancelOverrunJobs(Guid slotId) { }

    public void CancelAllJobs(Guid slotId) { }
}
