using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

// Refresh token của app. Chỉ lưu bản băm SHA-256 của token; token gốc chỉ có ở phía client.
// Bảng được tạo bằng docs/sql/002_refresh_token.sql
public partial class RefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    // null: còn hiệu lực. Có giá trị: đã đăng xuất hoặc đã được đổi lấy token mới
    public DateTime? RevokedAt { get; set; }

    // Token mới thay cho token này khi refresh (null nếu bị thu hồi vì đăng xuất)
    public Guid? ReplacedById { get; set; }

    public virtual User User { get; set; } = null!;
}
