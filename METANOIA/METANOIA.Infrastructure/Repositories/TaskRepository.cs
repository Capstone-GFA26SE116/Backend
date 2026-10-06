using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using DomainTask = METANOIA.Domain.Entities.Task;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class TaskRepository : ITaskRepository
    {
        private readonly AppDbContext _context;

        public TaskRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<bool> ProjectOwnedByUserAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Projects.AnyAsync(p => p.Id == projectId && p.UserId == userId, cancellationToken);
        }

        public async Task<IReadOnlyList<DomainTask>> GetByProjectIdAsync(Guid projectId, string? status, CancellationToken cancellationToken = default)
        {
            var query = _context.Tasks.Where(t => t.ProjectId == projectId);
            if (status is not null)
            {
                query = query.Where(t => t.Status == status);
            }

            return await query
                .OrderBy(t => t.Deadline)
                .ThenBy(t => t.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public Task<DomainTask?> GetByIdForUserAsync(Guid taskId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Tasks
                .FirstOrDefaultAsync(t => t.Id == taskId && t.Project.UserId == userId, cancellationToken);
        }

        public Task<bool> MilestoneBelongsToProjectAsync(Guid milestoneId, Guid projectId, CancellationToken cancellationToken = default)
        {
            return _context.Milestones.AnyAsync(m => m.Id == milestoneId && m.ProjectId == projectId, cancellationToken);
        }

        public Task<bool> DomainExistsAsync(Guid domainId, CancellationToken cancellationToken = default)
        {
            return _context.Domains.AnyAsync(d => d.Id == domainId, cancellationToken);
        }

        // Task đã có lịch sử học/làm việc thì không xóa, vì FocusSession, TaskExecutionMemory, KbiasUpdateLog đều RESTRICT
        public async Task<bool> HasHistoryAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            return await _context.FocusSessions.AnyAsync(f => f.TaskId == taskId, cancellationToken)
                || await _context.TaskExecutionMemories.AnyAsync(m => m.TaskId == taskId, cancellationToken)
                || await _context.KbiasUpdateLogs.AnyAsync(k => k.TaskId == taskId, cancellationToken);
        }

        public async Task AddAsync(DomainTask task, CancellationToken cancellationToken = default)
        {
            await _context.Tasks.AddAsync(task, cancellationToken);
        }

        public void Remove(DomainTask task)
        {
            _context.Tasks.Remove(task);
        }
    }
}
