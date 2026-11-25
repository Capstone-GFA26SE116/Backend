namespace METANOIA.Application.Dto
{
    public record GoogleEventBriefDto(string Title, DateTimeOffset Start, DateTimeOffset End);

    // Một slot đang chiếm giờ (Approved, Due, InProgress hoặc Overrun) của chính người dùng
    // TaskId rỗng với slot nhóm (khi đó có GroupTaskId); TaskTitle là tên việc cá nhân hoặc việc nhóm
    public record SlotOverlapDto(Guid SlotId, Guid? TaskId, string TaskTitle, string Status, DateTimeOffset Start, DateTimeOffset End, Guid? GroupTaskId = null);

    // Một slot đề xuất chồng giờ lên slot đang hoạt động đã có
    public record ProposalSlotOverlapDto(Guid SlotId, Guid? TaskId, string TaskTitle, DateTimeOffset Start, DateTimeOffset End, SlotOverlapDto With);

    // Một slot đề xuất trùng với sự kiện trên Google Calendar của người dùng
    public record SlotConflictDto(
        Guid SlotId, Guid? TaskId, string TaskTitle, DateTimeOffset Start, DateTimeOffset End, GoogleEventBriefDto Event);

    // Sửa giờ của một slot đề xuất (PUT /api/schedule/proposals/{proposalId}/slots/{slotId})
    public class ProposedSlotUpdateRequest
    {
        public DateTimeOffset StartTime { get; set; }

        public DateTimeOffset EndTime { get; set; }
    }

    public class ProposedSlotUpdateResultDto
    {
        public ScheduleSlotDto Slot { get; set; } = null!;

        // Gợi ý theo dữ liệu Google đã lưu (không gọi Google): sự kiện nào đang trùng giờ mới. Rỗng nếu không trùng hoặc chưa kết nối
        public List<SlotConflictDto> GoogleConflicts { get; set; } = new();
    }

    public class ProposalConflictsDto
    {
        // NotConnected: chưa kết nối Google nên không kiểm. Cached: kiểm theo dữ liệu đã lưu (chưa đồng bộ lại lúc này)
        public string GoogleCheck { get; set; } = "NotConnected";

        public List<SlotConflictDto> Conflicts { get; set; } = new();

        // Slot đề xuất chồng giờ lên slot đang hoạt động đã có (không cần kết nối Google). Duyệt sẽ trả 409 SLOT_OVERLAP nếu còn
        public List<ProposalSlotOverlapDto> Overlaps { get; set; } = new();
    }
}
