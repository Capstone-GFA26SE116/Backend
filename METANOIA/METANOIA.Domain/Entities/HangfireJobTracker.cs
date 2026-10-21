using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Domain.Entities
{
    public class HangfireJobTracker
    {
        public Guid Id { get; set; }

        public Guid ScheduleSlotId { get; set; }

        public string JobType { get; set; } = string.Empty;

        public string HangfireJobId { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }

        public virtual ScheduleSlot? ScheduleSlot { get; set; }
    }
}
