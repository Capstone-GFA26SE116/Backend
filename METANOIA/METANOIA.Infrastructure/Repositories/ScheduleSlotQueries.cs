using METANOIA.Domain.Entities;

namespace METANOIA.Infrastructure.Repositories
{
    // Một chỗ duy nhất quyết định "slot này của ai": slot nhóm lưu người đã xếp ở UserId; slot cá nhân suy ra từ Task -> Project -> UserId.
    // Mọi truy vấn lọc slot theo người dùng phải dùng hàm này để không bỏ sót slot nhóm.
    public static class ScheduleSlotQueries
    {
        public static IQueryable<ScheduleSlot> OwnedBy(this IQueryable<ScheduleSlot> query, Guid userId)
        {
            return query.Where(s => s.UserId == userId || (s.TaskId != null && s.Task!.Project.UserId == userId));
        }
    }
}
