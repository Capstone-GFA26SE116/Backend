namespace METANOIA.Application.Dto
{
    // Một thay đổi trạng thái do lần đồng bộ lại tạo ra (hoặc sẽ tạo ra khi chạy thử)
    public record StatusResyncChangeDto(string Kind, Guid Id, string Name, string From, string To);

    public class StatusResyncReportDto
    {
        // true: chỉ tính và báo cáo, KHÔNG lưu gì vào database
        public bool DryRun { get; set; }

        public int TasksChecked { get; set; }

        public int TasksChanged { get; set; }

        public int MilestonesChanged { get; set; }

        // Slot chưa diễn ra bị hủy vì task tự chuyển sang Done
        public int SlotsCancelled { get; set; }

        // Danh sách thay đổi (cắt ở 1000 dòng đầu, xem Truncated)
        public List<StatusResyncChangeDto> Changes { get; set; } = new();

        public bool Truncated { get; set; }
    }
}
