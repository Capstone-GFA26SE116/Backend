namespace METANOIA.Application.Dto
{
    // Báo cáo doanh thu (GET /api/admin/revenue). Đơn vị tiền là đồng (cùng kiểu long với PaymentTransaction.Amount), giờ UTC
    public class RevenueReportDto
    {
        // Khoảng thời gian: gồm From, không gồm To
        public DateTimeOffset From { get; set; }

        public DateTimeOffset To { get; set; }

        // day | month | plan
        public string GroupBy { get; set; } = null!;

        public RevenueTotalsDto Total { get; set; } = new();

        // Theo ngày hoặc tháng: đủ mọi mốc trong khoảng (kể cả mốc không có doanh thu); theo gói: gói doanh thu cao nhất lên đầu
        public List<RevenueRowDto> Rows { get; set; } = new();

        // Số giao dịch ĐƯỢC TẠO trong khoảng theo từng trạng thái (Succeeded, Pending, Failed, Cancelled, Expired), đủ 5 loại
        public Dictionary<string, int> TransactionsByStatus { get; set; } = new();
    }

    public class RevenueTotalsDto
    {
        // Tổng tiền các giao dịch Succeeded có PaidAt trong khoảng
        public long GrossRevenue { get; set; }

        // Tổng tiền hoàn đã hoàn tất (PaymentRefund Completed) có ProcessedAt trong khoảng
        public long RefundedAmount { get; set; }

        public long NetRevenue { get; set; }

        public int SucceededTransactions { get; set; }

        // Giá trị trung bình mỗi giao dịch thành công (làm tròn xuống); 0 nếu chưa có
        public long AverageOrderValue { get; set; }

        // Hóa đơn phát hành (IssuedAt) trong khoảng
        public int InvoicesIssued { get; set; }

        // Người dùng có giao dịch thành công ĐẦU TIÊN trong khoảng
        public int NewPayingUsers { get; set; }
    }

    public class RevenueRowDto
    {
        // Ngày (yyyy-MM-dd), tháng (yyyy-MM) hoặc tên gói
        public string Key { get; set; } = null!;

        public long Revenue { get; set; }

        public int Transactions { get; set; }

        public long Refunded { get; set; }

        public long Net { get; set; }

        // Tỷ trọng doanh thu gộp của dòng này trong tổng (0 đến 100, một chữ số thập phân); 0 nếu tổng bằng 0
        public decimal SharePercent { get; set; }
    }

    // Bộ lọc danh sách giao dịch (GET /api/payment-transactions). Mọi trường đều tùy chọn; không truyền gì thì trả như cũ
    public class PaymentTransactionFilter
    {
        public string? Status { get; set; }

        // Chỉ Admin dùng được; người dùng thường luôn chỉ thấy giao dịch của mình
        public Guid? UserId { get; set; }

        public Guid? PlanId { get; set; }

        // Theo ngày tạo giao dịch; gồm From, không gồm To
        public DateTimeOffset? From { get; set; }

        public DateTimeOffset? To { get; set; }

        // Truyền Page hoặc PageSize thì phân trang; tổng số dòng nằm ở header X-Total-Count
        public int? Page { get; set; }

        public int? PageSize { get; set; }

        public bool HasAny => Status is not null || UserId is not null || PlanId is not null || From is not null || To is not null || Page is not null || PageSize is not null;
    }

    // Bộ lọc danh sách hóa đơn (GET /api/invoices)
    public class InvoiceFilter
    {
        public Guid? UserId { get; set; }

        // Theo ngày phát hành; gồm From, không gồm To
        public DateTimeOffset? From { get; set; }

        public DateTimeOffset? To { get; set; }

        // Tìm trong số hóa đơn, tên gói, tên và email thanh toán
        public string? Search { get; set; }

        public int? Page { get; set; }

        public int? PageSize { get; set; }

        public bool HasAny => UserId is not null || From is not null || To is not null || !string.IsNullOrWhiteSpace(Search) || Page is not null || PageSize is not null;
    }
}
