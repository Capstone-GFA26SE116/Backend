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
    // /api/admin/users, /api/admin/ai-usage, /api/admin/focus-tracks, /api/admin/audit-logs và việc tự ghi AdminActionLog
    public class AdminTests
    {
        private static async Task<(HttpClient Admin, HttpClient User, Guid AdminId, Guid UserId)> SetupAsync(ApiFactory factory)
        {
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, adminAuth) = await TestHelpers.LoginAsync(factory, "admin-1");
            var (user, userAuth) = await TestHelpers.LoginAsync(factory, "user-1");
            return (admin, user, adminAuth.UserId, userAuth.UserId);
        }

        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static async Task<PagedResultDto<AdminAuditLogDto>> AuditLogsAsync(HttpClient admin, string query = "")
        {
            return (await admin.GetFromJsonAsync<PagedResultDto<AdminAuditLogDto>>($"/api/admin/audit-logs{query}"))!;
        }

        // ------------------------------------------------------------ Quyền

        [Theory]
        [InlineData("/api/admin/users")]
        [InlineData("/api/admin/ai-usage")]
        [InlineData("/api/admin/focus-tracks")]
        [InlineData("/api/admin/audit-logs")]
        public async Task AdminEndpoints_AreForbiddenForNormalUsers_AndUnauthorizedWithoutLogin(string url)
        {
            using var factory = new ApiFactory();
            var (_, user, _, _) = await SetupAsync(factory);

            var asUser = await user.GetAsync(url);
            var anonymous = await factory.CreateClient().GetAsync(url);

            Assert.Equal(HttpStatusCode.Forbidden, asUser.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        // ------------------------------------------------------------ Users

        [Fact]
        public async Task Users_List_ShowsPlan_AndSupportsSearchRoleAndPaging()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, userId) = await SetupAsync(factory);
            await TestHelpers.LoginAsync(factory, "user-2");

            var all = await admin.GetFromJsonAsync<PagedResultDto<AdminUserListItemDto>>("/api/admin/users");
            var search = await admin.GetFromJsonAsync<PagedResultDto<AdminUserListItemDto>>("/api/admin/users?search=USER-1");
            var admins = await admin.GetFromJsonAsync<PagedResultDto<AdminUserListItemDto>>("/api/admin/users?role=Admin");
            var firstPage = await admin.GetFromJsonAsync<PagedResultDto<AdminUserListItemDto>>("/api/admin/users?page=1&pageSize=2");

            Assert.Equal(3, all!.TotalCount);
            Assert.All(all.Items.Where(u => u.Role == "Freelancer"), u => Assert.Equal("Miễn phí", u.CurrentPlanName));
            Assert.Equal(userId, Assert.Single(search!.Items).Id);
            Assert.Equal("admin-1@test.com", Assert.Single(admins!.Items).Email);
            Assert.Equal(2, firstPage!.Items.Count);
            Assert.Equal(3, firstPage.TotalCount);
        }

        [Fact]
        public async Task Users_List_HidesDeletedAccounts_UnlessAsked()
        {
            using var factory = new ApiFactory();
            var (admin, user, _, userId) = await SetupAsync(factory);
            await user.DeleteAsync("/api/me");

            var normal = await admin.GetFromJsonAsync<PagedResultDto<AdminUserListItemDto>>("/api/admin/users");
            var withDeleted = await admin.GetFromJsonAsync<PagedResultDto<AdminUserListItemDto>>("/api/admin/users?includeDeleted=true");

            Assert.DoesNotContain(normal!.Items, u => u.Id == userId);
            Assert.NotNull(withDeleted!.Items.Single(u => u.Id == userId).DeletedAt);
        }

        [Fact]
        public async Task Users_Detail_ReturnsSubscriptionsPaymentsAndCounts()
        {
            using var factory = new ApiFactory();
            var (admin, user, _, userId) = await SetupAsync(factory);
            var plan = await (await admin.PostAsJsonAsync("/api/subscription-plans", new SubscriptionPlanRequestDto
            {
                Name = "Pro", Price = 99000, DurationDays = 30, AiCallLimit = 500, StorageLimitMb = 2048
            })).Content.ReadFromJsonAsync<SubscriptionPlanDto>();
            var checkout = await PaymentFlowTests.CheckoutAsync(user, plan!.Id);
            await PaymentFlowTests.PayAsync(factory, checkout);

            var detail = await admin.GetFromJsonAsync<AdminUserDetailDto>($"/api/admin/users/{userId}");

            Assert.Equal("Pro", detail!.CurrentPlanName);
            Assert.Equal(1, detail.ProjectCount);
            Assert.Equal(0, detail.TaskCount);
            Assert.Equal(1, detail.PaymentCount);
            Assert.Equal(99000, detail.TotalPaid);
            Assert.Equal("NotConnected", detail.GoogleCalendarStatus);
            Assert.Equal(2, detail.Subscriptions.Count);
            Assert.Contains(detail.Subscriptions, s => s.PlanName == "Pro" && s.Status == "Active" && s.AiCallLimit == 500);
            Assert.Contains(detail.Subscriptions, s => s.PlanName == "Miễn phí" && s.Status == "Expired");
            var payment = Assert.Single(detail.RecentPayments);
            Assert.Equal("Succeeded", payment.Status);
        }

        [Fact]
        public async Task Users_Detail_UnknownUser_Returns404()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, _) = await SetupAsync(factory);

            var response = await admin.GetAsync($"/api/admin/users/{Guid.NewGuid()}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Theory]
        [InlineData("page=0")]
        [InlineData("pageSize=0")]
        [InlineData("pageSize=101")]
        public async Task Users_List_RejectsBadPaging(string query)
        {
            using var factory = new ApiFactory();
            var (admin, _, _, _) = await SetupAsync(factory);

            var response = await admin.GetAsync($"/api/admin/users?{query}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // ------------------------------------------------------------ AI usage

        private static async Task SeedAiLogsAsync(ApiFactory factory, Guid userId)
        {
            await factory.SeedAsync(db =>
            {
                var subscriptionId = db.UserSubscriptions.Single(s => s.UserId == userId).Id;
                AiusageLog Log(string feature, string model, DateTime at, int input, int output, decimal cost, int latency, bool ok, bool charged) => new()
                {
                    Id = Guid.NewGuid(),
                    UserSubscriptionId = subscriptionId,
                    Feature = feature,
                    Model = model,
                    InputTokens = input,
                    OutputTokens = output,
                    Cost = cost,
                    LatencyMs = latency,
                    Succeeded = ok,
                    QuotaCharged = charged,
                    CreatedAt = at
                };

                db.AiusageLogs.AddRange(
                    Log("Chat", "model-a", new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc), 100, 50, 0.010m, 800, true, true),
                    Log("Chat", "model-a", new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc), 200, 100, 0.020m, 1200, false, false),
                    Log("Schedule", "model-b", new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc), 300, 150, 0.100m, 2000, true, true),
                    Log("Chat", "model-a", new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), 999, 999, 9.000m, 100, true, true));
            });
        }

        private const string Range = "from=2026-10-01T00:00:00Z&to=2026-10-31T00:00:00Z";

        [Fact]
        public async Task AiUsage_GroupsByFeature_WithTotals_AndIgnoresLogsOutsideRange()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, userId) = await SetupAsync(factory);
            await SeedAiLogsAsync(factory, userId);

            var report = await admin.GetFromJsonAsync<AiUsageReportDto>($"/api/admin/ai-usage?groupBy=feature&{Range}");

            Assert.Equal("feature", report!.GroupBy);
            Assert.Equal(new[] { "Schedule", "Chat" }, report.Rows.Select(r => r.Key).ToArray()); // tốn kém nhất trước
            var chat = report.Rows.Single(r => r.Key == "Chat");
            Assert.Equal(2, chat.Calls);
            Assert.Equal(1, chat.SucceededCalls);
            Assert.Equal(1, chat.FailedCalls);
            Assert.Equal(1, chat.QuotaChargedCalls);
            Assert.Equal(300, chat.InputTokens);
            Assert.Equal(150, chat.OutputTokens);
            Assert.Equal(0.030m, chat.Cost);
            Assert.Equal(1000, chat.AverageLatencyMs);
            Assert.Equal(3, report.Total.Calls);
            Assert.Equal(0.130m, report.Total.Cost);
        }

        [Fact]
        public async Task AiUsage_GroupsByModel_AndByDay()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, userId) = await SetupAsync(factory);
            await SeedAiLogsAsync(factory, userId);

            var byModel = await admin.GetFromJsonAsync<AiUsageReportDto>($"/api/admin/ai-usage?groupBy=model&{Range}");
            var byDay = await admin.GetFromJsonAsync<AiUsageReportDto>($"/api/admin/ai-usage?groupBy=day&{Range}");

            Assert.Equal(new[] { "model-b", "model-a" }, byModel!.Rows.Select(r => r.Key).ToArray());
            Assert.Equal(new[] { "2026-10-01", "2026-10-02" }, byDay!.Rows.Select(r => r.Key).ToArray());
            Assert.Equal(2, byDay.Rows[0].Calls);
            Assert.Equal(1, byDay.Rows[1].Calls);
        }

        [Fact]
        public async Task AiUsage_WithNoData_ReturnsEmptyReport_AndRejectsBadInput()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, _) = await SetupAsync(factory);

            var empty = await admin.GetFromJsonAsync<AiUsageReportDto>("/api/admin/ai-usage");
            var badGroup = await admin.GetAsync("/api/admin/ai-usage?groupBy=user");
            var reversed = await admin.GetAsync("/api/admin/ai-usage?from=2026-10-10T00:00:00Z&to=2026-10-01T00:00:00Z");
            var tooLong = await admin.GetAsync("/api/admin/ai-usage?from=2020-01-01T00:00:00Z&to=2026-01-01T00:00:00Z");

            Assert.Empty(empty!.Rows);
            Assert.Equal(0, empty.Total.Calls);
            Assert.Equal(HttpStatusCode.BadRequest, badGroup.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        }

        // ------------------------------------------------------------ Focus tracks

        private static FocusTrackRequestDto Track(string title = "Rain") => new()
        {
            Title = title,
            Artist = "Nature",
            FileUrl = "https://cdn.example.com/rain.mp3",
            DurationSeconds = 600,
            IsActive = true
        };

        [Fact]
        public async Task FocusTracks_CreateUpdateAndFilter()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, _) = await SetupAsync(factory);

            var created = await (await admin.PostAsJsonAsync("/api/admin/focus-tracks", Track()))
                .Content.ReadFromJsonAsync<FocusTrackDto>();
            var other = await (await admin.PostAsJsonAsync("/api/admin/focus-tracks", Track("Cafe")))
                .Content.ReadFromJsonAsync<FocusTrackDto>();
            var disable = Track("Rain");
            disable.IsActive = false;
            var updated = await (await admin.PutAsJsonAsync($"/api/admin/focus-tracks/{created!.Id}", disable))
                .Content.ReadFromJsonAsync<FocusTrackDto>();

            var activeOnly = await admin.GetFromJsonAsync<List<FocusTrackDto>>("/api/admin/focus-tracks?isActive=true");
            var everything = await admin.GetFromJsonAsync<List<FocusTrackDto>>("/api/admin/focus-tracks");
            var one = await admin.GetFromJsonAsync<FocusTrackDto>($"/api/admin/focus-tracks/{created.Id}");

            Assert.False(updated!.IsActive);
            Assert.Equal(other!.Id, Assert.Single(activeOnly!).Id);
            Assert.Equal(2, everything!.Count);
            Assert.Equal("Rain", one!.Title);
        }

        [Fact]
        public async Task FocusTracks_RejectInvalidInput_AndUnknownId()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, _) = await SetupAsync(factory);

            var noTitle = Track(" ");
            var badUrl = Track(); badUrl.FileUrl = "ftp://x/y.mp3";
            var notUrl = Track(); notUrl.FileUrl = "not a url";
            var zeroLength = Track(); zeroLength.DurationSeconds = 0;
            var tooLongTitle = Track(new string('x', 151));

            foreach (var request in new[] { noTitle, badUrl, notUrl, zeroLength, tooLongTitle })
            {
                var response = await admin.PostAsJsonAsync("/api/admin/focus-tracks", request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }

            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/focus-tracks/{Guid.NewGuid()}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/admin/focus-tracks/{Guid.NewGuid()}", Track())).StatusCode);
        }

        [Fact]
        public async Task FocusTracks_NormalUserCannotWrite()
        {
            using var factory = new ApiFactory();
            var (_, user, _, _) = await SetupAsync(factory);

            var response = await user.PostAsJsonAsync("/api/admin/focus-tracks", Track());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(0, await QueryAsync(factory, db => db.FocusTracks.CountAsync()));
        }

        // ------------------------------------------------------------ AdminActionLog được ghi tự động

        [Fact]
        public async Task AdminWrites_AreLogged_WithOldAndNewValues_AndTheGivenReason()
        {
            using var factory = new ApiFactory();
            var (admin, _, adminId, _) = await SetupAsync(factory);

            var created = await (await admin.PostAsJsonAsync("/api/domains", new DomainRequestDto { Name = "Writing", IsActive = true }))
                .Content.ReadFromJsonAsync<DomainDto>();

            var update = new HttpRequestMessage(HttpMethod.Put, $"/api/domains/{created!.Id}")
            {
                Content = JsonContent.Create(new DomainRequestDto { Name = "Copywriting", IsActive = false })
            };
            update.Headers.Add("X-Admin-Reason", "Đổi tên cho đúng nghiệp vụ");
            (await admin.SendAsync(update)).EnsureSuccessStatusCode();

            (await admin.DeleteAsync($"/api/domains/{created.Id}")).EnsureSuccessStatusCode();

            var logs = (await AuditLogsAsync(admin, $"?targetType=Domain&targetId={created.Id}")).Items;

            Assert.Equal(new[] { "Domain.Delete", "Domain.Update", "Domain.Create" }, logs.Select(l => l.Action).ToArray());
            Assert.All(logs, l => Assert.Equal(adminId, l.AdminId));
            Assert.All(logs, l => Assert.Equal("admin-1@test.com", l.AdminEmail));

            var createLog = logs.Single(l => l.Action == "Domain.Create");
            Assert.Null(createLog.OldValue);
            Assert.Equal("Writing", createLog.NewValue!.Value.GetProperty("Name").GetString());
            Assert.Equal("Không ghi lý do", createLog.Reason);

            var updateLog = logs.Single(l => l.Action == "Domain.Update");
            Assert.Equal("Writing", updateLog.OldValue!.Value.GetProperty("Name").GetString());
            Assert.Equal("Copywriting", updateLog.NewValue!.Value.GetProperty("Name").GetString());
            Assert.False(updateLog.NewValue.Value.GetProperty("IsActive").GetBoolean());
            Assert.Equal("Đổi tên cho đúng nghiệp vụ", updateLog.Reason);

            var deleteLog = logs.Single(l => l.Action == "Domain.Delete");
            Assert.Equal("Copywriting", deleteLog.OldValue!.Value.GetProperty("Name").GetString());
            Assert.Null(deleteLog.NewValue);
        }

        [Fact]
        public async Task UpdateLog_ContainsOnlyTheFieldsThatActuallyChanged()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, _) = await SetupAsync(factory);
            var created = await (await admin.PostAsJsonAsync("/api/admin/focus-tracks", Track()))
                .Content.ReadFromJsonAsync<FocusTrackDto>();
            var changed = Track();
            changed.DurationSeconds = 900;

            await admin.PutAsJsonAsync($"/api/admin/focus-tracks/{created!.Id}", changed);
            var log = (await AuditLogsAsync(admin, "?action=FocusTrack.Update")).Items.Single();

            Assert.Equal(new[] { "DurationSeconds" }, log.OldValue!.Value.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Equal(600, log.OldValue.Value.GetProperty("DurationSeconds").GetInt32());
            Assert.Equal(900, log.NewValue!.Value.GetProperty("DurationSeconds").GetInt32());
        }

        [Fact]
        public async Task UpdateWithNoRealChange_WritesNoLog()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, _) = await SetupAsync(factory);
            var created = await (await admin.PostAsJsonAsync("/api/admin/focus-tracks", Track()))
                .Content.ReadFromJsonAsync<FocusTrackDto>();

            await admin.PutAsJsonAsync($"/api/admin/focus-tracks/{created!.Id}", Track());

            Assert.Empty((await AuditLogsAsync(admin, "?action=FocusTrack.Update")).Items);
        }

        [Fact]
        public async Task OtherAdminEndpoints_AreLogged_IncludingSideEffectsOfRefundApproval()
        {
            using var factory = new ApiFactory();
            var (admin, user, _, _) = await SetupAsync(factory);
            var plan = await (await admin.PostAsJsonAsync("/api/subscription-plans", new SubscriptionPlanRequestDto
            {
                Name = "Pro", Price = 99000, DurationDays = 30, AiCallLimit = 500, StorageLimitMb = 2048
            })).Content.ReadFromJsonAsync<SubscriptionPlanDto>();
            var checkout = await PaymentFlowTests.CheckoutAsync(user, plan!.Id);
            await PaymentFlowTests.PayAsync(factory, checkout);
            var refund = await (await user.PostAsJsonAsync($"/api/payments/{checkout.PaymentId}/refunds", new RefundRequestDto { Reason = "Mua nhầm" }))
                .Content.ReadFromJsonAsync<RefundDto>();

            await admin.PostAsJsonAsync($"/api/refunds/{refund!.Id}/approve", new RefundProcessDto { Note = "OK" });
            var actions = (await AuditLogsAsync(admin, "?pageSize=100")).Items.Select(l => l.Action).ToList();

            Assert.Contains("SubscriptionPlan.Create", actions);
            Assert.Contains("PaymentRefund.Update", actions);
            Assert.Contains("UserSubscription.Update", actions); // gói bị thu hồi khi duyệt hoàn tiền
        }

        [Fact]
        public async Task ThingsThatAreNotAdminActions_AreNeverLogged()
        {
            using var factory = new ApiFactory();
            var (admin, user, _, _) = await SetupAsync(factory);
            var plan = await (await admin.PostAsJsonAsync("/api/subscription-plans", new SubscriptionPlanRequestDto
            {
                Name = "Pro", Price = 99000, DurationDays = 30, StorageLimitMb = 2048
            })).Content.ReadFromJsonAsync<SubscriptionPlanDto>();
            var before = (await AuditLogsAsync(admin)).TotalCount;

            // Người dùng tự mua gói, webhook xử lý thanh toán, chính admin mua gói như người dùng thường, đọc dữ liệu
            var checkout = await PaymentFlowTests.CheckoutAsync(user, plan!.Id);
            await PaymentFlowTests.PayAsync(factory, checkout);
            await PaymentFlowTests.CheckoutAsync(admin, plan.Id);
            await admin.GetAsync("/api/admin/users");

            Assert.Equal(before, (await AuditLogsAsync(admin)).TotalCount);
        }

        [Fact]
        public async Task AuditLogs_FilterByAdminAndAction_AndPaginate()
        {
            using var factory = new ApiFactory();
            var (admin, _, adminId, _) = await SetupAsync(factory);
            for (var i = 0; i < 5; i++)
            {
                await admin.PostAsJsonAsync("/api/admin/focus-tracks", Track($"Track {i}"));
            }

            var firstPage = await AuditLogsAsync(admin, "?action=FocusTrack.Create&page=1&pageSize=2");
            var lastPage = await AuditLogsAsync(admin, "?action=FocusTrack.Create&page=3&pageSize=2");
            var byAdmin = await AuditLogsAsync(admin, $"?adminId={adminId}&pageSize=100");
            var byStranger = await AuditLogsAsync(admin, $"?adminId={Guid.NewGuid()}");

            Assert.Equal(5, firstPage.TotalCount);
            Assert.Equal(2, firstPage.Items.Count);
            Assert.Single(lastPage.Items);
            Assert.Equal(5, byAdmin.TotalCount);
            Assert.Equal(0, byStranger.TotalCount);
        }

        [Fact]
        public async Task AuditLogs_RejectBadPagingAndRange()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, _) = await SetupAsync(factory);

            var badPage = await admin.GetAsync("/api/admin/audit-logs?page=0");
            var reversed = await admin.GetAsync("/api/admin/audit-logs?from=2026-10-10T00:00:00Z&to=2026-10-01T00:00:00Z");

            Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        }
    }
}
