using System;
using System.Collections.Generic;

namespace METANOIA.Application.Dto
{
    public class ScheduleProposalRequestDto
    {
        public List<Guid>? TaskIds { get; set; }

        public string Language { get; set; } = "vi";
    }

    public class ScheduleProposalDto
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public string Status { get; set; } = null!;

        public bool IsFeasible { get; set; }

        public int? MissingMinutes { get; set; }

        public string Explanation { get; set; } = null!;

        public DateTime CreatedAt { get; set; }

        public List<ScheduleSlotDto> Slots { get; set; } = new();
    }
}
