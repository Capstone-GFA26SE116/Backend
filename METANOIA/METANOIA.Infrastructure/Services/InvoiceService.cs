using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class InvoiceService : IInvoiceService
    {
        private const int MaxInvoiceNumberLength = 30;
        private const int MaxPlanNameLength = 50;
        private const int MaxBillingNameLength = 100;
        private const int MaxEmailLength = 255;

        private readonly IInvoiceRepository _invoiceRepository;
        private readonly IUnitOfWork _unitOfWork;

        public InvoiceService(IInvoiceRepository invoiceRepository, IUnitOfWork unitOfWork)
        {
            _invoiceRepository = invoiceRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken = default)
        {
            var invoices = await _invoiceRepository.GetAsync(isAdmin ? null : userId, cancellationToken);
            return invoices.Select(ToDto).ToList();
        }

        public async Task<InvoiceDto> GetInvoiceAsync(Guid userId, bool isAdmin, Guid invoiceId, CancellationToken cancellationToken = default)
        {
            var invoice = await GetExistingAsync(invoiceId, cancellationToken);
            if (!isAdmin)
            {
                var payment = await _invoiceRepository.GetPaymentAsync(invoice.PaymentTransactionId, cancellationToken);
                if (payment?.UserId != userId)
                {
                    throw new NotFoundException("Không tìm thấy hóa đơn.");
                }
            }

            return ToDto(invoice);
        }

        public async Task<InvoiceDto> CreateInvoiceAsync(InvoiceRequestDto request, CancellationToken cancellationToken = default)
        {
            var payment = await _invoiceRepository.GetPaymentAsync(request.PaymentTransactionId, cancellationToken)
                ?? throw new UserFriendlyException("Giao dịch thanh toán không tồn tại.");

            if (await _invoiceRepository.PaymentHasInvoiceAsync(request.PaymentTransactionId, Guid.Empty, cancellationToken))
            {
                throw new UserFriendlyException("Giao dịch này đã có hóa đơn.");
            }

            if (request.Amount != payment.Amount)
            {
                throw new UserFriendlyException("Số tiền hóa đơn phải khớp với số tiền của giao dịch thanh toán.");
            }

            var invoiceNumber = await ValidateCommonAsync(request.InvoiceNumber, request.PlanName, request.BillingName, request.BillingEmail, Guid.Empty, cancellationToken);
            ValidatePeriod(request.PeriodStart, request.PeriodEnd);

            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                PaymentTransactionId = request.PaymentTransactionId,
                InvoiceNumber = invoiceNumber,
                PlanName = request.PlanName.Trim(),
                Amount = request.Amount,
                BillingName = request.BillingName.Trim(),
                BillingEmail = request.BillingEmail.Trim(),
                PeriodStart = request.PeriodStart,
                PeriodEnd = request.PeriodEnd,
                IssuedAt = DateTime.UtcNow
            };

            await _invoiceRepository.AddAsync(invoice, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(invoice);
        }

        public async Task<InvoiceDto> UpdateInvoiceAsync(Guid invoiceId, InvoiceUpdateDto request, CancellationToken cancellationToken = default)
        {
            var invoice = await GetExistingAsync(invoiceId, cancellationToken);
            ValidateBilling(request.BillingName, request.BillingEmail);

            invoice.BillingName = request.BillingName.Trim();
            invoice.BillingEmail = request.BillingEmail.Trim();
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(invoice);
        }

        public async Task DeleteInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default)
        {
            var invoice = await GetExistingAsync(invoiceId, cancellationToken);
            _invoiceRepository.Remove(invoice);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task<Invoice> GetExistingAsync(Guid invoiceId, CancellationToken cancellationToken)
        {
            return await _invoiceRepository.GetByIdAsync(invoiceId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy hóa đơn.");
        }

        private async Task<string> ValidateCommonAsync(string invoiceNumber, string planName, string billingName, string billingEmail, Guid excludeId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(invoiceNumber) || invoiceNumber.Trim().Length > MaxInvoiceNumberLength)
            {
                throw new UserFriendlyException($"Số hóa đơn không được để trống và tối đa {MaxInvoiceNumberLength} ký tự.");
            }

            if (string.IsNullOrWhiteSpace(planName) || planName.Trim().Length > MaxPlanNameLength)
            {
                throw new UserFriendlyException($"Tên gói không được để trống và tối đa {MaxPlanNameLength} ký tự.");
            }

            ValidateBilling(billingName, billingEmail);

            var number = invoiceNumber.Trim();
            if (await _invoiceRepository.InvoiceNumberExistsAsync(number, excludeId, cancellationToken))
            {
                throw new UserFriendlyException("Số hóa đơn đã tồn tại.");
            }

            return number;
        }

        private static void ValidateBilling(string billingName, string billingEmail)
        {
            if (string.IsNullOrWhiteSpace(billingName) || billingName.Trim().Length > MaxBillingNameLength)
            {
                throw new UserFriendlyException($"Tên người mua không được để trống và tối đa {MaxBillingNameLength} ký tự.");
            }

            if (string.IsNullOrWhiteSpace(billingEmail)
                || billingEmail.Trim().Length > MaxEmailLength
                || !billingEmail.Contains('@'))
            {
                throw new UserFriendlyException("Email người mua không hợp lệ.");
            }
        }

        private static void ValidatePeriod(DateOnly start, DateOnly end)
        {
            if (end <= start)
            {
                throw new UserFriendlyException("Ngày kết thúc kỳ phải sau ngày bắt đầu kỳ.");
            }
        }

        private static InvoiceDto ToDto(Invoice invoice)
        {
            return new InvoiceDto
            {
                Id = invoice.Id,
                PaymentTransactionId = invoice.PaymentTransactionId,
                InvoiceNumber = invoice.InvoiceNumber,
                PlanName = invoice.PlanName,
                Amount = invoice.Amount,
                BillingName = invoice.BillingName,
                BillingEmail = invoice.BillingEmail,
                PeriodStart = invoice.PeriodStart,
                PeriodEnd = invoice.PeriodEnd,
                IssuedAt = UtcTime.ToApi(invoice.IssuedAt)
            };
        }
    }
}
