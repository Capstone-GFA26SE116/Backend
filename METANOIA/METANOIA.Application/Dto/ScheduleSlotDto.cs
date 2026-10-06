namespace METANOIA.Application.Dto
{
    public class ScheduleSlotDto
    {
        public Guid Id { get; set; }

        public Guid TaskId { get; set; }

        public Guid? SubTaskId { get; set; }

        public Guid? ScheduleProposalId { get; set; }

        public DateTimeOffset StartTime { get; set; }

        public DateTimeOffset EndTime { get; set; }

        public DateTimeOffset? OriginalStartTime { get; set; }

        public bool IsManualOverride { get; set; }

        public int ProposedMinutes { get; set; }

        public decimal? KbiasUsed { get; set; }

        public string? Explanation { get; set; }

        public string Status { get; set; } = null!;

        public string? GoogleEventId { get; set; }

        public bool SyncFailed { get; set; }
    }
}
