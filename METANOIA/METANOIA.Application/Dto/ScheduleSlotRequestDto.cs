namespace METANOIA.Application.Dto
{
    public class ScheduleSlotRequestDto
    {
        public Guid? SubTaskId { get; set; }

        public Guid? ScheduleProposalId { get; set; }

        public DateTimeOffset StartTime { get; set; }

        public DateTimeOffset EndTime { get; set; }

        public DateTimeOffset? OriginalStartTime { get; set; }

        public bool IsManualOverride { get; set; }

        public decimal? KbiasUsed { get; set; }

        public string? Explanation { get; set; }

        public string? Status { get; set; }
    }
}
