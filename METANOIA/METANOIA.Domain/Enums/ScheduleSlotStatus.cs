using System;

namespace METANOIA.Domain.Enums
{
    public enum ScheduleSlotStatus
    {
        Proposed,
        Approved,
        Due,
        InProgress,
        Done,
        Skipped,
        Missed,
        Overrun,
        Postponed,
        Cancelled
    }
}
