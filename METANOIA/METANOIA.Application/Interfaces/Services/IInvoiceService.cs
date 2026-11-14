using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IInvoiceService
    {
        Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken = default);

        // Có bộ lọc và phân trang tùy chọn. Trả kèm tổng số dòng khớp (trước khi phân trang)
        Task<(IReadOnlyList<InvoiceDto> Items, int TotalCount)> SearchInvoicesAsync(
            Guid userId, bool isAdmin, InvoiceFilter filter, CancellationToken cancellationToken = default);

        Task<InvoiceDto> GetInvoiceAsync(Guid userId, bool isAdmin, Guid invoiceId, CancellationToken cancellationToken = default);

        Task<InvoiceDto> CreateInvoiceAsync(InvoiceRequestDto request, CancellationToken cancellationToken = default);

        Task<InvoiceDto> UpdateInvoiceAsync(Guid invoiceId, InvoiceUpdateDto request, CancellationToken cancellationToken = default);

        Task DeleteInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    }
}
