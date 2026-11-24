using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Domain.Entities;

namespace METANOIA.Infrastructure.Services
{
    // Luật "slot của một người không được chồng giờ nhau": dựng DTO và ngoại lệ dùng chung cho tạo, sửa slot và duyệt đề xuất.
    // Chỉ slot đang chiếm giờ (Approved, Due, InProgress, Overrun) mới chặn; Proposed, Done, Skipped, Missed, Cancelled, Postponed thì không.
    internal static class SlotOverlap
    {
        public static SlotOverlapDto ToDto(ScheduleSlot slot) => new(
            slot.Id, slot.TaskId, slot.Task?.Title ?? string.Empty, slot.Status, UtcTime.ToApi(slot.StartTime), UtcTime.ToApi(slot.EndTime));

        public static SlotOverlapException Create(IReadOnlyList<ScheduleSlot> overlaps)
        {
            var titles = string.Join(", ", overlaps.Select(s => $"\"{s.Task?.Title}\"").Distinct());
            return new SlotOverlapException(
                $"Khung giờ này chồng lên {overlaps.Count} slot đang có ({titles}). Mỗi người chỉ làm một việc trong một khung giờ: hãy chọn giờ ngoài khoảng đó.",
                new { overlaps = overlaps.Select(ToDto).ToList() });
        }
    }
}
