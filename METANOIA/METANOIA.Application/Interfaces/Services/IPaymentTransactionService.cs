using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IPaymentTransactionService
    {
        // Người dùng thường chỉ thấy giao dịch của chính mình; Admin thấy tất cả
        Task<IReadOnlyList<PaymentTransactionDto>> GetPaymentsAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken = default);

        // Có bộ lọc và phân trang tùy chọn. Trả kèm tổng số dòng khớp (trước khi phân trang)
        Task<(IReadOnlyList<PaymentTransactionDto> Items, int TotalCount)> SearchPaymentsAsync(
            Guid userId, bool isAdmin, PaymentTransactionFilter filter, CancellationToken cancellationToken = default);

        Task<PaymentTransactionDto> GetPaymentAsync(Guid userId, bool isAdmin, Guid paymentId, CancellationToken cancellationToken = default);

        Task<PaymentTransactionDto> CreatePaymentAsync(PaymentTransactionRequestDto request, CancellationToken cancellationToken = default);

        Task<PaymentTransactionDto> UpdatePaymentAsync(Guid paymentId, PaymentTransactionRequestDto request, CancellationToken cancellationToken = default);

        Task DeletePaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);
    }
}
