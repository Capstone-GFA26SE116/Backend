using System;
using System.Collections.Generic;

namespace METANOIA.Application.Dto
{
    public class ScheduleProposalRequestDto
    {
        public List<Guid>? TaskIds { get; set; }

        public string Language { get; set; } = "vi";
    }

    public class ScheduleProposalDto
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public string Status { get; set; } = null!;

        public bool IsFeasible { get; set; }

        public int? MissingMinutes { get; set; }

        public string Explanation { get; set; } = null!;

        public DateTime CreatedAt { get; set; }

        public List<ScheduleSlotDto> Slots { get; set; } = new();

        // Chỉ có khi duyệt (POST .../approve). Checked: đã đồng bộ Google rồi kiểm. Stale: đồng bộ lỗi nên kiểm theo dữ liệu đã lưu.
        // NotConnected: chưa kết nối Google nên không kiểm
        public string? GoogleCheck { get; set; }

        // Các slot bị bỏ (không ghi) vì trùng Google khi duyệt với skipConflicts = true
        public List<SlotConflictDto>? SkippedConflicts { get; set; }
    }
}
