namespace METANOIA.Application.Dto
{
    // Một task của giai đoạn chưa Done hoặc Skipped, kèm số subtask còn ToDo (dùng để cảnh báo khi giao giai đoạn)
    public record MilestoneOpenTaskDto(Guid Id, string Title, string Status, int OpenSubTaskCount);
}
