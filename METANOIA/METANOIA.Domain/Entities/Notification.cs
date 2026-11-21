using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class Notification
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? ScheduleSlotId { get; set; }

    // Thông báo của nhánh Group: liên kết tới nhóm và việc để FE mở thẳng. Rỗng với thông báo cũ và thông báo của nhánh cá nhân
    // (cột thêm bởi docs/sql/005_group_tables.sql bước 3; chỉ khai báo cột, DB có FK ON DELETE SET NULL)
    public Guid? GroupId { get; set; }

    public Guid? GroupTaskId { get; set; }

    public string Type { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public DateTime ScheduledAt { get; set; }

    public DateTime? SentAt { get; set; }

    public bool IsRead { get; set; }

    public DateTime? ReadAt { get; set; }

    public virtual ScheduleSlot? ScheduleSlot { get; set; }

    public virtual User User { get; set; } = null!;
}
