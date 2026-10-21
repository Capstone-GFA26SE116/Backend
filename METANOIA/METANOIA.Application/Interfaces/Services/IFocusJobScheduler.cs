using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IFocusJobScheduler
    {
        Task RescheduleForEarlyStartAsync(
            Guid slotId,
            DateTimeOffset actualStart,
            CancellationToken cancellationToken = default);

        Task CancelOverrunJobsAsync(
            Guid slotId,
            CancellationToken cancellationToken = default);

        Task ScheduleSlotAsync(
            Guid slotId,
            CancellationToken cancellationToken = default);
    }
}
