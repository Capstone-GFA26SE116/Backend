namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IUnitOfWork
    {
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

        // Chạy `action` (có thể gọi SaveChanges nhiều lần) trong một transaction: lỗi thì rollback tất cả
        Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default);
    }
}
