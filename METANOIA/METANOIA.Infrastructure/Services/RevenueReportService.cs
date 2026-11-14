using System.Globalization;
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;

namespace METANOIA.Infrastructure.Services
{
    // Báo cáo doanh thu cho Admin. Doanh thu tính theo NGÀY THANH TOÁN (PaidAt) của giao dịch Succeeded; hoàn tiền tính vào kỳ
    // mà nó được xử lý xong (ProcessedAt). Mọi mốc ngày và tháng theo giờ UTC, giống báo cáo chi phí AI.
    public class RevenueReportService : IRevenueReportService
    {
        private const int DefaultRangeDays = 30;
        private const int MaxRangeDays = 366;
        private static readonly string[] Statuses = ["Succeeded", "Pending", "Failed", "Cancelled", "Expired"];
        private static readonly HashSet<string> Groupings = new() { "day", "month", "plan" };

        private readonly IRevenueRepository _repository;

        public RevenueReportService(IRevenueRepository repository)
        {
            _repository = repository;
        }

        public async Task<RevenueReportDto> GetReportAsync(string? groupBy, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default)
        {
            var grouping = string.IsNullOrWhiteSpace(groupBy) ? "day" : groupBy.Trim().ToLowerInvariant();
            if (!Groupings.Contains(grouping))
            {
                throw new UserFriendlyException("groupBy không hợp lệ. Chọn một trong: day, month, plan.");
            }

            var end = to ?? DateTimeOffset.UtcNow;
            var start = from ?? end.AddDays(-DefaultRangeDays);
            if (end <= start)
            {
                throw new UserFriendlyException("Khoảng thời gian không hợp lệ: 'to' phải lớn hơn 'from'.");
            }

            if (end - start > TimeSpan.FromDays(MaxRangeDays))
            {
                throw new UserFriendlyException($"Khoảng thời gian tối đa {MaxRangeDays} ngày.");
            }

            var fromUtc = start.UtcDateTime;
            var toUtc = end.UtcDateTime;

            var paid = await _repository.GetPaidAsync(fromUtc, toUtc, cancellationToken);
            var refunds = await _repository.GetCompletedRefundsAsync(fromUtc, toUtc, cancellationToken);
            var byStatus = await _repository.CountTransactionsByStatusAsync(fromUtc, toUtc, cancellationToken);

            var gross = paid.Sum(p => p.Amount);
            var refunded = refunds.Sum(r => r.Amount);

            var total = new RevenueTotalsDto
            {
                GrossRevenue = gross,
                RefundedAmount = refunded,
                NetRevenue = gross - refunded,
                SucceededTransactions = paid.Count,
                AverageOrderValue = paid.Count == 0 ? 0 : gross / paid.Count,
                InvoicesIssued = await _repository.CountInvoicesAsync(fromUtc, toUtc, cancellationToken),
                NewPayingUsers = await _repository.CountNewPayingUsersAsync(fromUtc, toUtc, cancellationToken)
            };

            return new RevenueReportDto
            {
                From = start,
                To = end,
                GroupBy = grouping,
                Total = total,
                Rows = BuildRows(grouping, fromUtc, toUtc, paid, refunds, gross),
                TransactionsByStatus = Statuses.ToDictionary(s => s, s => byStatus.GetValueOrDefault(s))
            };
        }

        private static List<RevenueRowDto> BuildRows(
            string grouping, DateTime fromUtc, DateTime toUtc,
            IReadOnlyList<RevenuePaidRow> paid, IReadOnlyList<RevenueRefundRow> refunds, long gross)
        {
            var rows = new Dictionary<string, RevenueRowDto>(StringComparer.Ordinal);

            RevenueRowDto Row(string key)
            {
                if (!rows.TryGetValue(key, out var row))
                {
                    row = new RevenueRowDto { Key = key };
                    rows[key] = row;
                }

                return row;
            }

            // Theo ngày hoặc tháng: tạo sẵn mọi mốc để biểu đồ không bị hụt
            if (grouping != "plan")
            {
                var cursor = grouping == "day"
                    ? fromUtc.Date
                    : new DateTime(fromUtc.Year, fromUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var last = toUtc.AddTicks(-1);

                while (cursor <= last)
                {
                    Row(KeyOf(grouping, cursor));
                    cursor = grouping == "day" ? cursor.AddDays(1) : cursor.AddMonths(1);
                }
            }

            foreach (var payment in paid)
            {
                var row = Row(grouping == "plan" ? payment.PlanName : KeyOf(grouping, payment.PaidAt));
                row.Revenue += payment.Amount;
                row.Transactions++;
            }

            foreach (var refund in refunds)
            {
                Row(grouping == "plan" ? refund.PlanName : KeyOf(grouping, refund.ProcessedAt)).Refunded += refund.Amount;
            }

            foreach (var row in rows.Values)
            {
                row.Net = row.Revenue - row.Refunded;
                row.SharePercent = gross == 0 ? 0 : Math.Round(row.Revenue * 100m / gross, 1);
            }

            return grouping == "plan"
                ? rows.Values.OrderByDescending(r => r.Revenue).ThenBy(r => r.Key, StringComparer.Ordinal).ToList()
                : rows.Values.OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
        }

        private static string KeyOf(string grouping, DateTime value) =>
            value.ToString(grouping == "day" ? "yyyy-MM-dd" : "yyyy-MM", CultureInfo.InvariantCulture);
    }
}
