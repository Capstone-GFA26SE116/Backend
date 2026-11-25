using METANOIA.Application.Dto;
using METANOIA.Domain.Entities;

namespace METANOIA.Infrastructure.Services
{
    // Một chỗ duy nhất đổi ScheduleSlot thành DTO, dùng cho slot cá nhân, slot nhóm và slot của đề xuất AI
    internal static class SlotMapper
    {
        public static ScheduleSlotDto ToDto(ScheduleSlot slot) => Map<ScheduleSlotDto>(slot);

        public static T Map<T>(ScheduleSlot slot) where T : ScheduleSlotDto, new()
        {
            return new T
            {
                Id = slot.Id,
                TaskId = slot.TaskId,
                SubTaskId = slot.SubTaskId,
                GroupTaskId = slot.GroupTaskId,
                GroupSubTaskId = slot.GroupSubTaskId,
                UserId = slot.UserId,
                ScheduleProposalId = slot.ScheduleProposalId,
                StartTime = UtcTime.ToApi(slot.StartTime),
                EndTime = UtcTime.ToApi(slot.EndTime),
                OriginalStartTime = UtcTime.ToApi(slot.OriginalStartTime),
                IsManualOverride = slot.IsManualOverride,
                ProposedMinutes = slot.ProposedMinutes,
                KbiasUsed = slot.KbiasUsed,
                Explanation = slot.Explanation,
                Status = slot.Status,
                GoogleEventId = slot.GoogleEventId,
                SyncFailed = slot.SyncFailed
            };
        }
    }
}
