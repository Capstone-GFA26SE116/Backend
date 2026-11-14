namespace METANOIA.Application.Interfaces.Repositories
{
    public record RevenuePaidRow(DateTime PaidAt, long Amount, Guid PlanId, string PlanName);

    public record RevenueRefundRow(DateTime ProcessedAt, long Amount, Guid PlanId, string PlanName);

    // Chỉ đọc, phục vụ báo cáo doanh thu. Mọi khoảng đều là [from, to)
    public interface IRevenueRepository
    {
        // Giao dịch Succeeded có PaidAt trong khoảng
        Task<IReadOnlyList<RevenuePaidRow>> GetPaidAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

        // PaymentRefund Completed có ProcessedAt (hoặc CreatedAt nếu thiếu) trong khoảng
        Task<IReadOnlyList<RevenueRefundRow>> GetCompletedRefundsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

        // Số giao dịch được tạo (CreatedAt) trong khoảng, theo trạng thái
        Task<Dictionary<string, int>> CountTransactionsByStatusAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

        Task<int> CountInvoicesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

        // Số người dùng có giao dịch Succeeded đầu tiên rơi vào khoảng
        Task<int> CountNewPayingUsersAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    }
}
