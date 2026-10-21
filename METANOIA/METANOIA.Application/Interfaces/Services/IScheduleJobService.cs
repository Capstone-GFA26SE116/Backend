using METANOIA.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IScheduleJobService
    {
        void ScheduleAllJobsForSlot(ScheduleSlot slot);
        void RescheduleSlotJobs(Guid slotId, DateTimeOffset actualStartTime);
        void CancelOverrunJobs(Guid slotId);
        void CancelAllJobs(Guid slotId);
    }
}
