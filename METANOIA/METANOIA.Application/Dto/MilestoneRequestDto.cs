namespace METANOIA.Application.Dto
{
    public class MilestoneRequestDto
    {
        public string Name { get; set; } = null!;

        public int OrderIndex { get; set; }

        public DateOnly? DueDate { get; set; }

        public string? Status { get; set; }

        public int RevisionCount { get; set; }

        // Giao giai đoạn (Delivered) khi còn task dở: lần đầu server trả 409 kèm danh sách; gửi lại với true để xác nhận vẫn giao
        public bool ConfirmOpenTasks { get; set; }
    }
}
