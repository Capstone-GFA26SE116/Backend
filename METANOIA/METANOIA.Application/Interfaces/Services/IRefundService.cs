using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IRefundService
    {
        Task<RefundDto> RequestRefundAsync(Guid userId, Guid paymentId, RefundRequestDto request, CancellationToken cancellationToken = default);

        // Người dùng thường chỉ thấy yêu cầu của mình; Admin thấy tất cả và có thể lọc theo status
        Task<IReadOnlyList<RefundDto>> GetRefundsAsync(Guid userId, bool isAdmin, string? status, CancellationToken cancellationToken = default);

        Task<RefundDto> GetRefundAsync(Guid userId, bool isAdmin, Guid refundId, CancellationToken cancellationToken = default);

        // Requested -> Approved: thu hồi gói đăng ký gắn với giao dịch
        Task<RefundDto> ApproveAsync(Guid refundId, RefundProcessDto request, CancellationToken cancellationToken = default);

        // Requested -> Rejected (bắt buộc có lý do)
        Task<RefundDto> RejectAsync(Guid refundId, RefundProcessDto request, CancellationToken cancellationToken = default);

        // Approved -> Completed sau khi admin đã chuyển khoản (bắt buộc có mã giao dịch ngân hàng)
        Task<RefundDto> CompleteAsync(Guid refundId, RefundProcessDto request, CancellationToken cancellationToken = default);
    }
}
