using System;
using System.Threading;
using System.Threading.Tasks;
using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IScheduleProposalService
    {
        Task<AiScheduleProposalResponseDto> ProposeScheduleAsync(Guid userId, ScheduleProposalRequestDto request, CancellationToken cancellationToken = default);

        Task<ScheduleProposalDto?> GetPendingProposalAsync(Guid userId, CancellationToken cancellationToken = default);

        // Có slot trùng Google và skipConflicts = false thì ném ScheduleConflictException (409), không đổi gì.
        // skipConflicts = true: bỏ các slot trùng (Cancelled), duyệt phần còn lại
        Task<ScheduleProposalDto> ApproveProposalAsync(Guid userId, Guid proposalId, bool skipConflicts, CancellationToken cancellationToken = default);

        // Chỉ sửa được slot còn Proposed của đề xuất còn Pending
        Task<ProposedSlotUpdateResultDto> UpdateProposedSlotAsync(
            Guid userId, Guid proposalId, Guid slotId, ProposedSlotUpdateRequest request, CancellationToken cancellationToken = default);

        // Slot Proposed thành Cancelled; slot đó không được ghi khi duyệt
        Task<ScheduleSlotDto> RejectProposedSlotAsync(Guid userId, Guid proposalId, Guid slotId, CancellationToken cancellationToken = default);

        // Xem trước các slot Proposed đang trùng Google (theo dữ liệu đã lưu, không đồng bộ)
        Task<ProposalConflictsDto> GetConflictsAsync(Guid userId, Guid proposalId, CancellationToken cancellationToken = default);
    }
}
