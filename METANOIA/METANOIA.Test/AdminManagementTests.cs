using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using METANOIA.Application.Dto;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using METANOIA.Test.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // Quản trị: báo cáo doanh thu, lọc giao dịch và hóa đơn, bảo vệ gói Miễn phí, Admin cuối cùng, lý do bắt buộc, nhật ký
    public class AdminManagementTests
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private sealed record Ctx(HttpClient Admin, HttpClient User, Guid AdminId, Guid UserId, Guid ProAiPlanId);

        private static async Task<Ctx> SetupAsync(ApiFactory factory, bool sendReasonByDefault = true)
        {
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, adminAuth) = await TestHelpers.LoginAsync(factory, "admin-1");
            var (user, userAuth) = await TestHelpers.LoginAsync(factory, "user-1");
            if (!sendReasonByDefault) admin.DefaultRequestHeaders.Remove("X-Admin-Reason");

            var proId = Guid.NewGuid();
            await factory.SeedAsync(db => db.SubscriptionPlans.Add(new SubscriptionPlan
            { Id = proId, Name = "Pro", Price = 100000, DurationDays = 30, StorageLimitMb = 512, IsActive = true }));
            return new Ctx(admin, user, adminAuth.UserId, userAuth.UserId, proId);
        }

        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static long _order = 1_000_000;

        private static PaymentTransaction Payment(Guid userId, Guid planId, long amount, string status, DateTime createdAt, DateTime? paidAt = null) => new()
        {
            Id = Guid.NewGuid(), UserId = userId, PlanId = planId, OrderCode = Interlocked.Increment(ref _order), Gateway = "PayOS",
            Amount = amount, Status = status, CreatedAt = createdAt, ExpiresAt = createdAt.AddMinutes(15), PaidAt = paidAt
        };

        private static string Iso(DateTime utc) => Uri.EscapeDataString(utc.ToString("yyyy-MM-ddTHH:mm:ss'Z'"));

        private static async Task<string> MessageAsync(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("message").GetString()!;
        }

        // ------------------------------------------------------------ Báo cáo doanh thu

        [Theory]
        [InlineData("/api/admin/revenue")]
        public async Task Revenue_IsForbiddenForUsers_AndUnauthorizedWithoutLogin(string url)
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);

            Assert.Equal(HttpStatusCode.Forbidden, (await c.User.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
        }

        // 3 ngày: ngày 1 thanh toán 100.000 (Pro) và 50.000 (Free), ngày 3 thanh toán 100.000 (Pro); ngày 2 không có gì
        private static async Task<(DateTime Day1, DateTime Day3)> SeedRevenueAsync(ApiFactory factory, Ctx c)
        {
            var day1 = new DateTime(2026, 3, 10, 5, 0, 0, DateTimeKind.Utc);
            var day3 = day1.AddDays(2).AddHours(3);
            await factory.SeedAsync(db =>
            {
                var a = Payment(c.UserId, c.ProAiPlanId, 100_000, "Succeeded", day1.AddMinutes(-5), day1);
                var b = Payment(c.UserId, ApiFactory.FreePlanId, 50_000, "Succeeded", day1.AddMinutes(-4), day1.AddHours(1));
                var d = Payment(c.UserId, c.ProAiPlanId, 100_000, "Succeeded", day3.AddMinutes(-5), day3);
                db.PaymentTransactions.AddRange(a, b, d,
                    Payment(c.UserId, c.ProAiPlanId, 100_000, "Pending", day1.AddMinutes(-3)),
                    Payment(c.UserId, c.ProAiPlanId, 100_000, "Failed", day1.AddMinutes(-2)));
                db.Invoices.Add(new Invoice
                {
                    Id = Guid.NewGuid(), PaymentTransactionId = a.Id, InvoiceNumber = "INV-1", PlanName = "Pro", Amount = 100_000,
                    BillingName = "A", BillingEmail = "a@x.com", PeriodStart = new DateOnly(2026, 3, 10), PeriodEnd = new DateOnly(2026, 4, 9), IssuedAt = day1
                });
                db.PaymentRefunds.Add(new PaymentRefund
                {
                    Id = Guid.NewGuid(), PaymentTransactionId = d.Id, Amount = 30_000, Reason = "Khách yêu cầu", Source = "User",
                    Status = "Completed", CreatedAt = day3.AddHours(1), ProcessedAt = day3.AddHours(2)
                });
                db.PaymentRefunds.Add(new PaymentRefund
                {
                    Id = Guid.NewGuid(), PaymentTransactionId = a.Id, Amount = 99_000, Reason = "Chưa xử lý", Source = "User",
                    Status = "Requested", CreatedAt = day3
                });
            });
            return (day1, day3);
        }

        private static string RangeQuery(DateTime day1, string groupBy) =>
            $"?groupBy={groupBy}&from={Iso(day1.Date)}&to={Iso(day1.Date.AddDays(3))}";

        [Fact]
        public async Task Revenue_ByDay_SumsOnlySucceededPayments_ByPaidAt_AndFillsEmptyDays()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);
            var (day1, _) = await SeedRevenueAsync(factory, c);

            var report = (await c.Admin.GetFromJsonAsync<RevenueReportDto>($"/api/admin/revenue{RangeQuery(day1, "day")}", Json))!;

            Assert.Equal("day", report.GroupBy);
            Assert.Equal(250_000, report.Total.GrossRevenue);           // 100.000 + 50.000 + 100.000, Pending và Failed không tính
            Assert.Equal(30_000, report.Total.RefundedAmount);          // chỉ hoàn tiền Completed
            Assert.Equal(220_000, report.Total.NetRevenue);
            Assert.Equal(3, report.Total.SucceededTransactions);
            Assert.Equal(83_333, report.Total.AverageOrderValue);
            Assert.Equal(1, report.Total.InvoicesIssued);
            Assert.Equal(1, report.Total.NewPayingUsers);

            Assert.Equal(new[] { "2026-03-10", "2026-03-11", "2026-03-12" }, report.Rows.Select(r => r.Key).ToArray());
            Assert.Equal(new long[] { 150_000, 0, 100_000 }, report.Rows.Select(r => r.Revenue).ToArray());
            Assert.Equal(new[] { 2, 0, 1 }, report.Rows.Select(r => r.Transactions).ToArray());
            Assert.Equal(new long[] { 0, 0, 30_000 }, report.Rows.Select(r => r.Refunded).ToArray());
            Assert.Equal(70_000, report.Rows[2].Net);
            Assert.Equal(60m, report.Rows[0].SharePercent);

            Assert.Equal(3, report.TransactionsByStatus["Succeeded"]);
            Assert.Equal(1, report.TransactionsByStatus["Pending"]);
            Assert.Equal(1, report.TransactionsByStatus["Failed"]);
            Assert.Equal(0, report.TransactionsByStatus["Expired"]);
        }

        [Fact]
        public async Task Revenue_ByPlan_AndByMonth()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);
            var (day1, _) = await SeedRevenueAsync(factory, c);

            var byPlan = (await c.Admin.GetFromJsonAsync<RevenueReportDto>($"/api/admin/revenue{RangeQuery(day1, "plan")}", Json))!;
            Assert.Equal(new[] { "Pro", "Miễn phí" }, byPlan.Rows.Select(r => r.Key).ToArray());   // doanh thu cao nhất lên đầu
            Assert.Equal(new long[] { 200_000, 50_000 }, byPlan.Rows.Select(r => r.Revenue).ToArray());
            Assert.Equal(30_000, byPlan.Rows[0].Refunded);

            var byMonth = (await c.Admin.GetFromJsonAsync<RevenueReportDto>($"/api/admin/revenue{RangeQuery(day1, "month")}", Json))!;
            Assert.Equal("2026-03", Assert.Single(byMonth.Rows).Key);
            Assert.Equal(250_000, byMonth.Rows[0].Revenue);
        }

        [Fact]
        public async Task Revenue_EmptyPeriod_IsAllZero_AndInvalidInputsReturn400()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);
            var day = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var empty = (await c.Admin.GetFromJsonAsync<RevenueReportDto>($"/api/admin/revenue{RangeQuery(day, "day")}", Json))!;
            Assert.Equal(0, empty.Total.GrossRevenue);
            Assert.Equal(0, empty.Total.AverageOrderValue);
            Assert.Equal(3, empty.Rows.Count);
            Assert.All(empty.Rows, r => Assert.Equal(0, r.SharePercent));

            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.GetAsync("/api/admin/revenue?groupBy=year")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.GetAsync($"/api/admin/revenue?from={Iso(day.AddDays(5))}&to={Iso(day)}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.GetAsync($"/api/admin/revenue?from={Iso(day)}&to={Iso(day.AddDays(400))}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await c.Admin.GetAsync("/api/admin/revenue")).StatusCode);   // mặc định 30 ngày gần nhất
        }

        // ------------------------------------------------------------ Lọc giao dịch và hóa đơn

        [Fact]
        public async Task PaymentTransactions_FilterAndPaging_KeepOldBehaviorWithoutParameters()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);
            var (day1, day3) = await SeedRevenueAsync(factory, c);

            var all = (await c.Admin.GetFromJsonAsync<List<PaymentTransactionDto>>("/api/payment-transactions", Json))!;
            Assert.Equal(5, all.Count);   // không tham số: như cũ

            var succeeded = (await c.Admin.GetFromJsonAsync<List<PaymentTransactionDto>>("/api/payment-transactions?status=Succeeded", Json))!;
            Assert.Equal(3, succeeded.Count);

            var free = (await c.Admin.GetFromJsonAsync<List<PaymentTransactionDto>>($"/api/payment-transactions?planId={ApiFactory.FreePlanId}", Json))!;
            Assert.Single(free);

            var day3Only = (await c.Admin.GetFromJsonAsync<List<PaymentTransactionDto>>(
                $"/api/payment-transactions?from={Iso(day3.Date)}&to={Iso(day3.Date.AddDays(1))}", Json))!;
            Assert.Single(day3Only);

            var page = await c.Admin.GetAsync("/api/payment-transactions?page=2&pageSize=2");
            Assert.Equal(2, (await page.Content.ReadFromJsonAsync<List<PaymentTransactionDto>>(Json))!.Count);
            Assert.Equal("5", page.Headers.GetValues("X-Total-Count").Single());

            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.GetAsync("/api/payment-transactions?status=Lạ")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.GetAsync("/api/payment-transactions?pageSize=500")).StatusCode);
        }

        [Fact]
        public async Task PaymentTransactions_NormalUsersOnlySeeTheirOwn_EvenWithAUserIdFilter()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);
            var (other, otherAuth) = await TestHelpers.LoginAsync(factory, "user-2");
            await factory.SeedAsync(db => db.PaymentTransactions.AddRange(
                Payment(c.UserId, c.ProAiPlanId, 1, "Succeeded", DateTime.UtcNow, DateTime.UtcNow),
                Payment(otherAuth.UserId, c.ProAiPlanId, 2, "Succeeded", DateTime.UtcNow, DateTime.UtcNow)));

            var mine = (await c.User.GetFromJsonAsync<List<PaymentTransactionDto>>($"/api/payment-transactions?userId={otherAuth.UserId}", Json))!;

            Assert.Single(mine);
            Assert.Equal(c.UserId, mine[0].UserId);   // userId của người khác bị bỏ qua

            var byAdmin = (await c.Admin.GetFromJsonAsync<List<PaymentTransactionDto>>($"/api/payment-transactions?userId={otherAuth.UserId}", Json))!;
            Assert.Single(byAdmin);
            Assert.Equal(otherAuth.UserId, byAdmin[0].UserId);
        }

        [Fact]
        public async Task Invoices_FilterBySearchAndDate_AndPaging()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);
            var (day1, _) = await SeedRevenueAsync(factory, c);
            await factory.SeedAsync(db =>
            {
                var p = Payment(c.UserId, c.ProAiPlanId, 100_000, "Succeeded", day1, day1);
                db.PaymentTransactions.Add(p);
                db.Invoices.Add(new Invoice
                {
                    Id = Guid.NewGuid(), PaymentTransactionId = p.Id, InvoiceNumber = "INV-2", PlanName = "Pro", Amount = 100_000,
                    BillingName = "Công ty B", BillingEmail = "b@x.com", PeriodStart = new DateOnly(2026, 3, 10), PeriodEnd = new DateOnly(2026, 4, 9),
                    IssuedAt = day1.AddDays(5)
                });
            });

            Assert.Equal(2, (await c.Admin.GetFromJsonAsync<List<InvoiceDto>>("/api/invoices", Json))!.Count);
            Assert.Single((await c.Admin.GetFromJsonAsync<List<InvoiceDto>>("/api/invoices?search=b@x.com", Json))!);
            Assert.Single((await c.Admin.GetFromJsonAsync<List<InvoiceDto>>($"/api/invoices?from={Iso(day1.Date.AddDays(3))}", Json))!);

            var page = await c.Admin.GetAsync("/api/invoices?page=1&pageSize=1");
            Assert.Single((await page.Content.ReadFromJsonAsync<List<InvoiceDto>>(Json))!);
            Assert.Equal("2", page.Headers.GetValues("X-Total-Count").Single());
        }

        // ------------------------------------------------------------ Quản lý gói

        [Fact]
        public async Task PlanList_HidesInactivePlansFromUsers_ButAdminSeesEverything()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);
            await factory.SeedAsync(db => db.SubscriptionPlans.Add(new SubscriptionPlan
            { Id = Guid.NewGuid(), Name = "Cũ đã tắt", Price = 1, DurationDays = 30, StorageLimitMb = 10, IsActive = false }));

            var forUser = (await c.User.GetFromJsonAsync<List<SubscriptionPlanDto>>("/api/subscription-plans", Json))!;
            var forAdmin = (await c.Admin.GetFromJsonAsync<List<SubscriptionPlanDto>>("/api/subscription-plans", Json))!;

            Assert.DoesNotContain(forUser, p => p.Name == "Cũ đã tắt");
            Assert.All(forUser, p => Assert.True(p.IsActive));
            Assert.Contains(forAdmin, p => p.Name == "Cũ đã tắt");
        }

        private static SubscriptionPlanRequestDto FreeRequest(Action<SubscriptionPlanRequestDto>? edit = null)
        {
            var request = new SubscriptionPlanRequestDto { Name = "Miễn phí", Price = 0, DurationDays = 30, AiCallLimit = 30, StorageLimitMb = 100, ProjectLimit = 3, IsActive = true };
            edit?.Invoke(request);
            return request;
        }

        [Fact]
        public async Task FreePlan_CannotBeDeactivated_MadePaid_OrDeleted_ButLimitsAndNameCanChange()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);
            var url = $"/api/subscription-plans/{ApiFactory.FreePlanId}";

            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.PutAsJsonAsync(url, FreeRequest(r => r.IsActive = false))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.PutAsJsonAsync(url, FreeRequest(r => r.Price = 1000))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.DeleteAsync(url)).StatusCode);

            var ok = await c.Admin.PutAsJsonAsync(url, FreeRequest(r => { r.Name = "Starter"; r.ProjectLimit = 5; r.StorageLimitMb = 200; }));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var plan = await QueryAsync(factory, db => db.SubscriptionPlans.AsNoTracking().SingleAsync(p => p.Id == ApiFactory.FreePlanId));
            Assert.Equal("Starter", plan.Name);
            Assert.True(plan.IsActive);
            Assert.Equal(0, plan.Price);
        }

        // ------------------------------------------------------------ Admin cuối cùng

        [Fact]
        public async Task LastAdmin_CannotDeleteTheirOwnAccount_ButCanWhenAnotherAdminExists()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);

            var blocked = await c.Admin.DeleteAsync("/api/me");
            Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
            Assert.Contains("Admin cuối cùng", await MessageAsync(blocked));
            Assert.Null((await QueryAsync(factory, db => db.Users.AsNoTracking().SingleAsync(u => u.Id == c.AdminId))).DeletedAt);

            await factory.SeedUserWithRoleAsync("admin-2", "Admin");
            Assert.Equal(HttpStatusCode.NoContent, (await c.Admin.DeleteAsync("/api/me")).StatusCode);

            // Người dùng thường tự xóa vẫn bình thường
            Assert.Equal(HttpStatusCode.NoContent, (await c.User.DeleteAsync("/api/me")).StatusCode);
        }

        // ------------------------------------------------------------ Lý do bắt buộc và nhật ký

        [Fact]
        public async Task SensitiveAdminActions_RequireAReason_AndChangeNothingWithout()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory, sendReasonByDefault: false);

            // Sửa gói
            var planUrl = $"/api/subscription-plans/{c.ProAiPlanId}";
            var edit = new SubscriptionPlanRequestDto { Name = "Pro", Price = 123_000, DurationDays = 30, StorageLimitMb = 512, IsActive = true };
            var noReason = await c.Admin.PutAsJsonAsync(planUrl, edit);
            Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
            Assert.Contains("lý do", await MessageAsync(noReason));
            Assert.Equal(100_000, (await QueryAsync(factory, db => db.SubscriptionPlans.AsNoTracking().SingleAsync(p => p.Id == c.ProAiPlanId))).Price);

            // Xóa người dùng
            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.DeleteAsync($"/api/admin/users/{c.UserId}")).StatusCode);
            Assert.Null((await QueryAsync(factory, db => db.Users.AsNoTracking().SingleAsync(u => u.Id == c.UserId))).DeletedAt);

            // Đổi role cần lý do; đổi tên thì không
            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.PutAsJsonAsync($"/api/admin/users/{c.UserId}", new AdminUserRequestDto { Role = "Admin" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await c.Admin.PutAsJsonAsync($"/api/admin/users/{c.UserId}", new AdminUserRequestDto { FullName = "Tên mới" })).StatusCode);

            // Thêm lý do thì được, và lý do nằm trong nhật ký
            var withReason = new HttpRequestMessage(HttpMethod.Put, planUrl) { Content = JsonContent.Create(edit) };
            withReason.Headers.Add("X-Admin-Reason", "Tăng giá theo bảng giá mới");
            Assert.Equal(HttpStatusCode.OK, (await c.Admin.SendAsync(withReason)).StatusCode);

            var logs = (await c.Admin.GetFromJsonAsync<PagedResultDto<AdminAuditLogDto>>($"/api/admin/audit-logs?targetType=SubscriptionPlan&targetId={c.ProAiPlanId}", Json))!;
            var log = Assert.Single(logs.Items);
            Assert.Equal("Tăng giá theo bảng giá mới", log.Reason);
            Assert.Equal(123_000, log.NewValue!.Value.GetProperty("Price").GetInt64());
        }

        [Fact]
        public async Task Reason_CanBeSentPercentEncoded_SoVietnameseTextWorksFromBrowsers()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory, sendReasonByDefault: false);
            var edit = new SubscriptionPlanRequestDto { Name = "Pro", Price = 111_000, DurationDays = 30, StorageLimitMb = 512, IsActive = true };

            var request = new HttpRequestMessage(HttpMethod.Put, $"/api/subscription-plans/{c.ProAiPlanId}") { Content = JsonContent.Create(edit) };
            request.Headers.Add("X-Admin-Reason", Uri.EscapeDataString("thay đổi giá"));   // "thay%20%C4%91%E1%BB%95i%20gi%C3%A1": chỉ ASCII, trình duyệt gửi được
            Assert.Equal(HttpStatusCode.OK, (await c.Admin.SendAsync(request)).StatusCode);

            var logs = (await c.Admin.GetFromJsonAsync<PagedResultDto<AdminAuditLogDto>>($"/api/admin/audit-logs?targetId={c.ProAiPlanId}", Json))!;
            Assert.Equal("thay đổi giá", Assert.Single(logs.Items).Reason);   // lưu lại đã giải mã
        }

        [Fact]
        public async Task Swagger_ShowsTheReasonHeader_RequiredOnSensitiveAdminEndpoints()
        {
            using var factory = new ApiFactory();
            using var doc = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json"));
            var paths = doc.RootElement.GetProperty("paths");

            JsonElement Header(string path, string method) => paths.GetProperty(path).GetProperty(method).GetProperty("parameters")
                .EnumerateArray().Single(p => p.GetProperty("name").GetString() == "X-Admin-Reason");

            Assert.True(Header("/api/subscription-plans/{planId}", "delete").GetProperty("required").GetBoolean());
            Assert.False(Header("/api/admin/status-resync", "post").TryGetProperty("required", out var optional) && optional.GetBoolean());
            Assert.False(paths.GetProperty("/api/subscription-plans").GetProperty("get").TryGetProperty("parameters", out var getParams)
                && getParams.EnumerateArray().Any(p => p.GetProperty("name").GetString() == "X-Admin-Reason"));
        }

        [Fact]
        public async Task StatusResync_NeedsAReasonOnlyWhenApplying_AndLeavesASummaryLog()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory, sendReasonByDefault: false);

            // Chạy thử không cần lý do và không để lại nhật ký
            Assert.Equal(HttpStatusCode.OK, (await c.Admin.PostAsync("/api/admin/status-resync", null)).StatusCode);
            Assert.Empty(await QueryAsync(factory, db => db.AdminActionLogs.AsNoTracking().Where(l => l.Action == "StatusResync.Run").ToListAsync()));

            // Chạy thật thiếu lý do: 400
            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.PostAsync("/api/admin/status-resync?dryRun=false", null)).StatusCode);
            Assert.Empty(await QueryAsync(factory, db => db.AdminActionLogs.AsNoTracking().Where(l => l.Action == "StatusResync.Run").ToListAsync()));

            var apply = new HttpRequestMessage(HttpMethod.Post, "/api/admin/status-resync?dryRun=false");
            apply.Headers.Add("X-Admin-Reason", "Đồng bộ sau khi nâng cấp luật trạng thái");
            Assert.Equal(HttpStatusCode.OK, (await c.Admin.SendAsync(apply)).StatusCode);

            var log = Assert.Single(await QueryAsync(factory, db => db.AdminActionLogs.AsNoTracking().Where(l => l.Action == "StatusResync.Run").ToListAsync()));
            Assert.Equal(c.AdminId, log.AdminId);
            Assert.Equal("Đồng bộ sau khi nâng cấp luật trạng thái", log.Reason);
            Assert.Contains("TasksChecked", log.NewValue);
        }
    }
}
