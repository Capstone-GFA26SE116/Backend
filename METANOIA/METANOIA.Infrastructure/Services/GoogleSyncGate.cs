using System.Collections.Concurrent;

namespace METANOIA.Infrastructure.Services
{
    // Google thường báo liên tiếp nhiều thông báo cho một thay đổi. Cổng này xếp các lần đồng bộ của cùng một user
    // thành hàng đợi để chúng không chạy song song (cùng syncToken sẽ sinh event trùng).
    public class GoogleSyncGate
    {
        private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

        public async Task<T> RunAsync<T>(Guid userId, Func<Task<T>> action)
        {
            var gate = _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                return await action();
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
