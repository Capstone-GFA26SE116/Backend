using System.Linq.Expressions;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class AiUsageRepository : IAiUsageRepository
    {
        private readonly AppDbContext _context;

        public AiUsageRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<AiUsageRowDto>> SummarizeAsync(
            string groupBy, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            var logs = _context.AiusageLogs.Where(l => l.CreatedAt >= from && l.CreatedAt < to);

            return groupBy switch
            {
                "feature" => await GroupAsync(logs, l => l.Feature, key => key, cancellationToken),
                "model" => await GroupAsync(logs, l => l.Model, key => key, cancellationToken),
                "day" => await GroupAsync(logs, l => l.CreatedAt.Date, key => key.ToString("yyyy-MM-dd"), cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(groupBy), groupBy, "groupBy phải là feature, model hoặc day.")
            };
        }

        public async Task<AiUsageRowDto> TotalAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            var rows = await GroupAsync(
                _context.AiusageLogs.Where(l => l.CreatedAt >= from && l.CreatedAt < to),
                _ => 1,
                _ => "Tổng",
                cancellationToken);

            return rows.FirstOrDefault() ?? new AiUsageRowDto { Key = "Tổng" };
        }

        private static async Task<IReadOnlyList<AiUsageRowDto>> GroupAsync<TKey>(
            IQueryable<AiusageLog> logs,
            Expression<Func<AiusageLog, TKey>> keySelector,
            Func<TKey, string> format,
            CancellationToken cancellationToken)
        {
            var rows = await logs
                .GroupBy(keySelector)
                .Select(g => new
                {
                    g.Key,
                    Calls = g.Count(),
                    Succeeded = g.Count(l => l.Succeeded),
                    QuotaCharged = g.Count(l => l.QuotaCharged),
                    InputTokens = g.Sum(l => (long)l.InputTokens),
                    OutputTokens = g.Sum(l => (long)l.OutputTokens),
                    Cost = g.Sum(l => l.Cost),
                    AverageLatency = g.Average(l => (double)l.LatencyMs)
                })
                .ToListAsync(cancellationToken);

            return rows.Select(r => new AiUsageRowDto
            {
                Key = format(r.Key),
                Calls = r.Calls,
                SucceededCalls = r.Succeeded,
                FailedCalls = r.Calls - r.Succeeded,
                QuotaChargedCalls = r.QuotaCharged,
                InputTokens = r.InputTokens,
                OutputTokens = r.OutputTokens,
                Cost = r.Cost,
                AverageLatencyMs = Math.Round(r.AverageLatency, 1)
            }).ToList();
        }
    }
}
