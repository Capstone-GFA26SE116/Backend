using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using DomainTask = METANOIA.Domain.Entities.Task;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Đồng bộ lại trạng thái cho dữ liệu tạo trước khi có luật liền mạch (xem docs/STATUS_WORKFLOW.md).
    // Dùng đúng TaskProgressService như luồng thường nên không có bản luật thứ hai để lệch.
    public class StatusResyncService : IStatusResyncService
    {
        private const int BatchSize = 100;
        private const int MaxListed = 1000;

        private readonly AppDbContext _context;
        private readonly ITaskProgressService _progress;

        public StatusResyncService(AppDbContext context, ITaskProgressService progress)
        {
            _context = context;
            _progress = progress;
        }

        public async Task<StatusResyncReportDto> ResyncAsync(bool dryRun, CancellationToken cancellationToken = default)
        {
            var report = new StatusResyncReportDto { DryRun = dryRun };

            // Task đã đóng (Done, Skipped) không đụng tới: người dùng đã chốt, mở lại là việc của họ
            var taskIds = await _context.Tasks.AsNoTracking()
                .Where(t => t.Status != StatusRules.Done && t.Status != StatusRules.Skipped)
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);
            report.TasksChecked = taskIds.Count;

            foreach (var batch in taskIds.Chunk(BatchSize))
            {
                var results = new List<ProgressResult>();
                foreach (var id in batch)
                {
                    results.Add(await _progress.RefreshTaskAsync(id, cancellationToken));
                }

                await CommitBatchAsync(report, results, dryRun, cancellationToken);
            }

            // Giai đoạn Planned đã có task bắt đầu hoặc xong (kể cả task đã Done bị bỏ qua ở trên) thì lên InProgress
            var milestoneIds = await _context.Milestones.AsNoTracking()
                .Where(m => m.Status == StatusRules.Planned
                    && m.Tasks.Any(t => t.Status == StatusRules.InProgress || t.Status == StatusRules.Done))
                .Select(m => m.Id)
                .ToListAsync(cancellationToken);

            foreach (var batch in milestoneIds.Chunk(BatchSize))
            {
                foreach (var id in batch)
                {
                    await _progress.RefreshMilestoneAsync(id, cancellationToken);
                }

                await CommitBatchAsync(report, new List<ProgressResult>(), dryRun, cancellationToken);
            }

            return report;
        }

        // Ghi nhận thay đổi từ ChangeTracker; chạy thật thì lưu rồi làm các việc sau lưu (hủy job, báo AI); xong thì xóa theo dõi
        private async Task CommitBatchAsync(StatusResyncReportDto report, List<ProgressResult> results, bool dryRun, CancellationToken cancellationToken)
        {
            Collect(report);

            if (!dryRun)
            {
                await _context.SaveChangesAsync(cancellationToken);
                foreach (var result in results)
                {
                    await _progress.AfterCommitAsync(result, cancellationToken);
                }
            }

            _context.ChangeTracker.Clear();
        }

        private void Collect(StatusResyncReportDto report)
        {
            foreach (var entry in _context.ChangeTracker.Entries<DomainTask>().Where(e => e.State == EntityState.Modified))
            {
                var from = entry.Property(t => t.Status).OriginalValue;
                var to = entry.Entity.Status;
                if (from == to) continue;
                report.TasksChanged++;
                Add(report, new StatusResyncChangeDto("Task", entry.Entity.Id, entry.Entity.Title, from, to));
            }

            foreach (var entry in _context.ChangeTracker.Entries<Milestone>().Where(e => e.State == EntityState.Modified))
            {
                var from = entry.Property(m => m.Status).OriginalValue;
                var to = entry.Entity.Status;
                if (from == to) continue;
                report.MilestonesChanged++;
                Add(report, new StatusResyncChangeDto("Milestone", entry.Entity.Id, entry.Entity.Name, from, to));
            }

            report.SlotsCancelled += _context.ChangeTracker.Entries<ScheduleSlot>()
                .Count(e => e.State == EntityState.Modified && e.Entity.Status == "Cancelled" && e.Property(s => s.Status).OriginalValue != "Cancelled");
        }

        private static void Add(StatusResyncReportDto report, StatusResyncChangeDto change)
        {
            if (report.Changes.Count < MaxListed) report.Changes.Add(change);
            else report.Truncated = true;
        }
    }
}
