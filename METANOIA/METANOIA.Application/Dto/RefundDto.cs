namespace METANOIA.Application.Dto
{
    public class RefundDto
    {
        public Guid Id { get; set; }

        public Guid PaymentTransactionId { get; set; }

        public Guid UserId { get; set; }

        public long Amount { get; set; }

        public string Reason { get; set; } = null!;

        public string Source { get; set; } = null!;

        public string Status { get; set; } = null!;

        public string? AdminNote { get; set; }

        public string? BankReference { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset? ProcessedAt { get; set; }
    }

    public class RefundRequestDto
    {
        public string Reason { get; set; } = null!;
    }

    public class RefundProcessDto
    {
        public string? Note { get; set; }

        // Mã giao dịch ngân hàng của lần chuyển khoản hoàn tiền (bắt buộc khi Complete)
        public string? BankReference { get; set; }
    }
}
