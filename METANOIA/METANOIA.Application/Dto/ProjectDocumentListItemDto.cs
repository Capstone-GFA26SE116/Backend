namespace METANOIA.Application.Dto
{
    // Tài liệu trong danh sách gom nhiều dự án: thêm dự án và chủ sở hữu để giao diện nhóm và hiển thị
    public class ProjectDocumentListItemDto : ProjectDocumentDto
    {
        public string ProjectName { get; set; } = null!;

        // Người sở hữu dự án chứa tài liệu
        public Guid UserId { get; set; }
    }
}
