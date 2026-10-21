using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using METANOIA.Domain.Enums;

namespace METANOIA.Application.Dto
{

    public sealed record FocusSelfReportRequest(
        [Required] Guid TaskId,
        Guid? ScheduleSlotId,
        DateTimeOffset StartedAt,
        DateTimeOffset EndedAt,
        FocusStopReason StopReason,
        string? Note
    );
}
