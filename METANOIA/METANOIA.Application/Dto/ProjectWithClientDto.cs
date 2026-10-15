namespace METANOIA.Application.Dto
{
    // Một dòng kết quả của GET /api/projects và GET /api/projects/filter: dự án kèm khách hàng (nếu có)
    public class ProjectWithClientDto
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public DateOnly? StartDate { get; set; }

        public DateOnly? Deadline { get; set; }

        public string Status { get; set; } = null!;

        // null nếu dự án không thuộc khách hàng nào
        public Guid? ClientId { get; set; }

        // Thông tin khách hàng của dự án; null khi dự án không có khách hàng (khi đó ClientId cũng là null)
        public ProjectClientDto? Client { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    // Thông tin gọn của khách hàng đi kèm dự án. Chi tiết đầy đủ ở GET /api/clients/{id}
    public class ProjectClientDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public string? ContactEmail { get; set; }

        public string? Phone { get; set; }
    }
}
