namespace METANOIA.Application.Dto
{
    public class ScheduleSlotDto
    {
        public Guid Id { get; set; }

        // Slot cá nhân: TaskId. Slot nhóm: TaskId rỗng, có GroupTaskId (và GroupSubTaskId), UserId là người đã xếp slot
        public Guid? TaskId { get; set; }

        public Guid? SubTaskId { get; set; }

        public Guid? GroupTaskId { get; set; }

        public Guid? GroupSubTaskId { get; set; }

        public Guid? UserId { get; set; }

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
