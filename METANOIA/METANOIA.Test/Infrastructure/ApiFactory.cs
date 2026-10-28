using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Services;
using METANOIA.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test.Infrastructure
{
    public class ApiFactory : WebApplicationFactory<Program>
    {
        public const string FrontendOrigin = "http://localhost:5173";
        public const string CalendarRedirectUri = "https://localhost/fe-test.html";
        public const string VapidPublicKey = "test-vapid-public-key";
        public const string JwtKey = "test-only-signing-key-0123456789-abcdefghijklmnop";

        // Id gói Miễn phí được seed khi có user đăng nhập lần đầu (giống dòng seed ở Supabase)
        public static readonly Guid FreePlanId = Guid.Parse("1b6f3a2e-0c4d-4f8a-9e21-5d7c8b9a0f01");

        // Bật để giả lập lỗi ở bước tạo project "Cá nhân" khi khởi tạo user mới
        public bool FailOnProjectCreate { get; set; }

        // Mặc định bật hoàn tiền để các test hoàn tiền hiện có chạy; test "hoàn tiền tắt" đặt false
        public bool RefundsEnabled { get; set; } = true;

        // Thay repository hoàn tiền bằng bản ném lỗi khi bị gọi, để chứng minh luồng thanh toán không đụng tới nó
        public bool ForbidRefundRepository { get; set; }

        // Máy chủ token Google giả và bộ ghi log, dùng cho test lỗi đồng bộ Google Calendar
        public FakeGoogleHttpHandler GoogleHttp { get; } = new();

        // Thay Hangfire: ghi lại việc hẹn và hủy job. Đồng hồ chỉnh tay cho các test về thời gian làm việc
        public FakeScheduleJobService Jobs { get; } = new();

        public ManualTimeProvider Clock { get; } = new();

        public CapturingLoggerProvider Logs { get; } = new();

        private readonly string _databaseName = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            // Program.cs đọc Hangfire:Enabled ngay lúc dựng host, trước khi AddInMemoryCollection bên dưới được áp dụng,
            // nên phải truyền bằng UseSetting. Trong test không có Postgres cho Hangfire; job được thay bằng FakeScheduleJobService.
            builder.UseSetting("Hangfire:Enabled", "false");

            // Tương tự: CORS cũng được dựng bằng giá trị đọc sớm, nên không được phụ thuộc vào appsettings.json cục bộ (file bị gitignore)
            builder.UseSetting("Frontend:AllowedOrigins:0", FrontendOrigin);

            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = JwtKey,
                ["Jwt:Issuer"] = "METANOIA",
                ["Jwt:Audience"] = "METANOIA.Client",
                ["Jwt:ExpiryMinutes"] = "60",
                ["Google:ClientId"] = "test-client-id",
                ["Google:ClientSecret"] = "test-client-secret",
                ["Google:CalendarRedirectUri"] = "https://localhost/api/google-calendar/callback",
                ["Frontend:AllowedOrigins:0"] = FrontendOrigin,
                ["Frontend:CalendarConnectedRedirectUri"] = CalendarRedirectUri,
                ["Storage:RootPath"] = Path.Combine(Path.GetTempPath(), "metanoia-tests", _databaseName),
                ["Subscription:FreePlanId"] = FreePlanId.ToString(),
                ["Hangfire:Enabled"] = "false",
                ["VapidSettings:PublicKey"] = VapidPublicKey,
                ["Payments:ReconciliationEnabled"] = "false",
                ["Payments:LinkExpiryMinutes"] = "15",
                ["Payments:RefundWindowDays"] = "7",
                ["Payments:RefundsEnabled"] = RefundsEnabled ? "true" : "false"
            }));

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>((serviceProvider, options) => options
                    .UseInMemoryDatabase(_databaseName)
                    .AddInterceptors(serviceProvider.GetRequiredService<AdminAuditSaveChangesInterceptor>()));

                services.RemoveAll<IScheduleJobService>();
                services.AddSingleton<IScheduleJobService>(Jobs);

                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);

                services.RemoveAll<IGoogleTokenValidator>();
                services.AddScoped<IGoogleTokenValidator, FakeGoogleTokenValidator>();

                services.RemoveAll<IUserSetupRepository>();
                services.AddScoped<IUserSetupRepository>(sp =>
                    new FailingUserSetupRepository(sp.GetRequiredService<AppDbContext>(), () => FailOnProjectCreate));

                services.RemoveAll<IGoogleCalendarSyncService>();
                services.AddScoped<IGoogleCalendarSyncService>(sp => new RecordingGoogleSyncService(
                    GoogleSync, ActivatorUtilities.CreateInstance<GoogleCalendarSyncService>(sp)));

                if (ForbidRefundRepository)
                {
                    services.RemoveAll<IPaymentRefundRepository>();
                    services.AddScoped<IPaymentRefundRepository, ForbiddenPaymentRefundRepository>();
                }

                services.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(() => GoogleHttp);
                services.AddLogging(logging => logging.AddProvider(Logs));

                services.RemoveAll<IPayOSClient>();
                services.AddSingleton<IPayOSClient>(PayOS);
            });
        }

        public FakePayOSClient PayOS { get; } = new();

        // Ghi lại các lần đồng bộ Google để test webhook; mặc định vẫn chạy bản thật
        public GoogleSyncRecorder GoogleSync { get; } = new();

        public async Task EnsureDefaultRoleAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (!await db.Roles.AnyAsync(r => r.Name == "Freelancer"))
            {
                db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = "Freelancer" });
                await db.SaveChangesAsync();
            }

            if (!await db.SubscriptionPlans.AnyAsync(p => p.Id == FreePlanId))
            {
                db.SubscriptionPlans.Add(new SubscriptionPlan
                {
                    Id = FreePlanId,
                    Name = "Miễn phí",
                    Price = 0,
                    DurationDays = 30,
                    AiCallLimit = 30,
                    StorageLimitMb = 100,
                    ProjectLimit = 3,
                    IsActive = true
                });
                await db.SaveChangesAsync();
            }
        }

        public async Task SeedUserWithRoleAsync(string subject, string roleName)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
            if (role is null)
            {
                role = new Role { Id = Guid.NewGuid(), Name = roleName };
                db.Roles.Add(role);
            }

            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                RoleId = role.Id,
                Email = $"{subject}@test.com",
                FullName = $"Test {subject}",
                GoogleSubjectId = subject,
                TimeZone = "Asia/Ho_Chi_Minh"
            });
            await db.SaveChangesAsync();
        }

        public async Task SeedAsync(Action<AppDbContext> seed)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            seed(db);
            await db.SaveChangesAsync();
        }
    }
}
