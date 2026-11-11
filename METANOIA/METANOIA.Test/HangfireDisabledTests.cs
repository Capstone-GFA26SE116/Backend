using METANOIA.Application.Interfaces.Services;
using METANOIA.Infrastructure.Services;
using METANOIA.Test.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // Hangfire:Enabled = false (chạy local không muốn tạo schema Hangfire) vẫn phải khởi động được ở môi trường Development.
    // Development bật kiểm tra toàn bộ đăng ký DI lúc dựng host, nên bản ScheduleJobService thật (cần Hangfire) không được còn trong danh sách.
    public class HangfireDisabledTests
    {
        [Fact]
        public async Task HangfireDisabled_StartsUp_AndUsesNullScheduleJobService()
        {
            using var factory = new ApiFactory { KeepProgramJobService = true };

            // Truy cập Services buộc host được dựng; lỗi đăng ký DI sẽ ném ở đây
            using var scope = factory.Services.CreateScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IScheduleJobService>();

            Assert.IsType<NullScheduleJobService>(jobs);

            // Gọi các hàm hẹn và hủy job không được ném lỗi dù Hangfire tắt
            jobs.CancelAllJobs(Guid.NewGuid());
            await Task.CompletedTask;
        }
    }
}
