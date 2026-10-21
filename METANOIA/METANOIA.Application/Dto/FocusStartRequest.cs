using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Application.Dto
{
    public sealed record FocusStartRequest(
        [Required] Guid TaskId,
        Guid? SubTaskId,
        Guid? ScheduleSlotId,
        Guid? FocusTrackId
    );
}
