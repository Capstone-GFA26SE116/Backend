using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class SubTaskRepository : ISubTaskRepository
    {
        private readonly AppDbContext _context;

        public SubTaskRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<bool> TaskOwnedByUserAsync(Guid taskId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Tasks.AnyAsync(t => t.Id == taskId && t.Project.UserId == userId, cancellationToken);
        }

        public async Task<IReadOnlyList<SubTask>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            return await _context.SubTasks
                .Where(s => s.TaskId == taskId)
                .OrderBy(s => s.OrderIndex)
                .ToListAsync(cancellationToken);
        }

        public Task<SubTask?> GetByIdForUserAsync(Guid subTaskId, Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.SubTasks
                .FirstOrDefaultAsync(s => s.Id == subTaskId && s.Task.Project.UserId == userId, cancellationToken);
        }

        public Task<bool> OrderIndexTakenAsync(Guid taskId, int orderIndex, Guid excludeId, CancellationToken cancellationToken = default)
        {
            return _context.SubTasks.AnyAsync(
                s => s.TaskId == taskId && s.OrderIndex == orderIndex && s.Id != excludeId,
                cancellationToken);
        }

        // FocusSession và TaskExecutionMemory đều tham chiếu SubTask với RESTRICT
        public async Task<bool> HasHistoryAsync(Guid subTaskId, CancellationToken cancellationToken = default)
        {
            return await _context.FocusSessions.AnyAsync(f => f.SubTaskId == subTaskId, cancellationToken)
                || await _context.TaskExecutionMemories.AnyAsync(m => m.SubTaskId == subTaskId, cancellationToken);
        }

        public async Task AddAsync(SubTask subTask, CancellationToken cancellationToken = default)
        {
            await _context.SubTasks.AddAsync(subTask, cancellationToken);
        }

        public void Remove(SubTask subTask)
        {
            _context.SubTasks.Remove(subTask);
        }
    }
}
