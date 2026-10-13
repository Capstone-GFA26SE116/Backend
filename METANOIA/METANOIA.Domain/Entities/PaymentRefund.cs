using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

// Yêu cầu hoàn tiền cho một PaymentTransaction. PayOS không có API hoàn tiền cho merchant,
// nên admin chuyển khoản tay rồi đánh dấu Completed. Bảng này được tạo bằng docs/sql/001_payment_refund.sql
public partial class PaymentRefund
{
    public Guid Id { get; set; }

    public Guid PaymentTransactionId { get; set; }

    public long Amount { get; set; }

    public string Reason { get; set; } = null!;

    // User: khách tự yêu cầu; System: hệ thống tự mở (tiền về sau khi đơn đã hủy, sai số tiền...)
    public string Source { get; set; } = null!;

    // Requested -> Approved -> Completed, hoặc Requested -> Rejected
    public string Status { get; set; } = null!;

    public string? AdminNote { get; set; }

    public string? BankReference { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public virtual PaymentTransaction PaymentTransaction { get; set; } = null!;
}
