namespace METANOIA.Application.Dto
{
    public class CheckoutRequestDto
    {
        public Guid PlanId { get; set; }
    }

    public class CheckoutResultDto
    {
        public Guid PaymentId { get; set; }

        public long OrderCode { get; set; }

        // Pending: mở CheckoutUrl để trả tiền. Succeeded: giao dịch cũ đã được thanh toán, không cần trả nữa
        public string Status { get; set; } = null!;

        public string? CheckoutUrl { get; set; }

        public string? QrCode { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }

        // true nếu người dùng đã có link chưa hết hạn cho gói này và hệ thống trả lại link cũ
        public bool Reused { get; set; }
    }
}
