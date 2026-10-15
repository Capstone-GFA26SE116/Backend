using System;
using System.Collections.Generic;

namespace METANOIA.Application.Dto
{
    // --- FR-17: Learning DTOs ---

    public class AiTaskCompletedRequestDto
    {
        public Guid TaskId { get; set; }
    }

    public class AiLearningResponseDto
    {
        public bool Learned { get; set; }

        public string? Reason { get; set; }

        public decimal? KOld { get; set; }

        public decimal? KNew { get; set; }

        public int? SampleIndex { get; set; }
    }

    // --- FR-10: Schedule Proposal DTOs ---

    public class AiScheduleProposeRequestDto
    {
        public List<Guid>? TaskIds { get; set; }

        public string Language { get; set; } = "vi";
    }

    public class AiScheduleSlotDto
    {
        public Guid SlotId { get; set; }

        public Guid TaskId { get; set; }

        public Guid? SubTaskId { get; set; }

        public DateTimeOffset Start { get; set; }

        public DateTimeOffset End { get; set; }

        public int ProposedMinutes { get; set; }

        public string? Explanation { get; set; }
    }

    public class AiWarningDto
    {
        public Guid? TaskId { get; set; }

        public string Type { get; set; } = null!;

        public int? NeededMinutes { get; set; }

        public int? FreeMinutes { get; set; }

        public string Message { get; set; } = null!;
    }

    public class AiScheduleProposalResponseDto
    {
        public Guid? ProposalId { get; set; }

        public bool IsFeasible { get; set; }

        public int? MissingMinutes { get; set; }

        public string Explanation { get; set; } = null!;

        public List<AiScheduleSlotDto> Slots { get; set; } = new();

        public List<AiWarningDto> Warnings { get; set; } = new();
    }
}
