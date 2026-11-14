using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IRevenueReportService
    {
        // groupBy: day (mặc định) | month | plan. from/to mặc định là 30 ngày gần nhất, tối đa 366 ngày
        Task<RevenueReportDto> GetReportAsync(string? groupBy, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default);
    }
}
