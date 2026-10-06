namespace METANOIA.Application.Dto
{
    // Hóa đơn đã phát hành chỉ cho sửa thông tin người mua; số tiền và kỳ thanh toán giữ nguyên
    public class InvoiceUpdateDto
    {
        public string BillingName { get; set; } = null!;

        public string BillingEmail { get; set; } = null!;
    }
}
