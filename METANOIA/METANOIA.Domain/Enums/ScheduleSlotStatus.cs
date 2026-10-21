using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Domain.Enums
{
    public enum ScheduleSlotStatus
    {
        Proposed,
        Approved,
        Due,         // Mới thêm
        InProgress,  // Mới thêm
        Done,
        Skipped,
        Missed,
        Overrun,     // Mới thêm
        Postponed
    }
}
