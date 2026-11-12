using METANOIA.Domain.Entities;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IFocusSessionRepository
    {
        // Phiên đang chạy (EndedAt == null) của user, không kèm dữ liệu liên quan
        Task<FocusSession?> GetRunningAsync(Guid userId);

        // Như trên nhưng kèm Task và ScheduleSlot (để trả tên task và biết đã quá giờ dự kiến chưa)
        Task<FocusSession?> GetRunningWithDetailsAsync(Guid userId);

        // Phiên theo id, kèm Task và Project (để kiểm tra chủ sở hữu)
        Task<FocusSession?> GetByIdAsync(Guid sessionId);

        // Mọi phiên đã kết thúc của một task, trừ một phiên (dùng khi cộng T_act của task)
        Task<List<FocusSession>> GetEndedSessionsByTaskIdAsync(Guid taskId, Guid excludeSessionId);

        // Phiên AutoTimeout đã kết thúc nhưng chưa được xác nhận, kèm Task. Mới nhất trước.
        // Phiên người dùng đã loại bỏ (ghi chú đánh dấu) do service lọc tiếp.
        Task<List<FocusSession>> GetUnconfirmedAutoTimeoutAsync(Guid userId);

        // Mọi phiên của một task (kể cả chưa xác nhận, đã loại bỏ), cũ trước. Dùng cho màn hình theo dõi
        Task<List<FocusSession>> GetByTaskIdAsync(Guid taskId);

        // Slot còn phiên ĐÁNG TIN nào khác (đã kết thúc, và là Timer, SelfReported hoặc AutoTimeout đã xác nhận) không
        Task<bool> HasCountedSessionForSlotAsync(Guid slotId, Guid excludeSessionId);

        System.Threading.Tasks.Task AddAsync(FocusSession session);
    }
}
