using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using METANOIA.Test.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // Hạn mức theo gói: số dự án (ProjectLimit) và dung lượng file (StorageLimitMb). Vượt thì chặn TẠO MỚI, không xóa gì
    public class PlanLimitTests
    {
        private const long Mb = 1024L * 1024;

        private static async Task<(HttpClient Client, Guid UserId)> LoginAsync(ApiFactory factory, string subject = "user-1")
        {
            var (client, auth) = await TestHelpers.LoginAsync(factory, subject);
            return (client, auth.UserId);
        }

        // Đổi gói Active của người dùng sang một gói có hạn mức đã cho
        private static async Task UsePlanAsync(ApiFactory factory, Guid userId, int? projectLimit, int storageMb, string name = "Gói thử")
        {
            var planId = Guid.NewGuid();
            await factory.SeedAsync(db =>
            {
                db.SubscriptionPlans.Add(new SubscriptionPlan
                {
                    Id = planId, Name = $"{name}-{planId:N}"[..20], Price = 0, DurationDays = 30,
                    StorageLimitMb = storageMb, ProjectLimit = projectLimit, IsActive = true
                });
                db.UserSubscriptions.Single(s => s.UserId == userId && s.Status == "Active").PlanId = planId;
            });
        }

        private static async Task<HttpResponseMessage> CreateProjectAsync(HttpClient client, string name) =>
            await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = name });

        private static async Task<Guid> NewProjectAsync(HttpClient client, string name)
        {
            var response = await CreateProjectAsync(client, name);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ProjectDto>())!.Id;
        }

        private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid projectId, long bytes)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(new byte[bytes]);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "File", "tep.bin");
            return client.PostAsync($"/api/projects/{projectId}/documents/files", form);
        }

        // Thân lỗi là JSON đã escape ký tự Unicode: lấy trường message ra để so sánh chữ có dấu
        private static async Task<string> ErrorAsync(HttpResponseMessage response)
        {
            using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("message").GetString()!;
        }

        private static int StoredFiles(ApiFactory factory)
        {
            var root = factory.Services.GetRequiredService<IConfiguration>()["Storage:RootPath"]!;
            return Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length : 0;
        }

        // ------------------------------------------------------------ Số dự án

        [Fact]
        public async Task FreePlan_AllowsThreeProjects_AndBlocksTheFourth_WithAnUpgradeHint()
        {
            using var factory = new ApiFactory();
            var (client, _) = await LoginAsync(factory);   // đã có dự án "Cá nhân" mặc định, không tính vào hạn mức

            for (var i = 1; i <= 3; i++) await NewProjectAsync(client, $"Dự án {i}");
            var fourth = await CreateProjectAsync(client, "Dự án 4");

            Assert.Equal(HttpStatusCode.BadRequest, fourth.StatusCode);
            var message = await ErrorAsync(fourth);
            Assert.Contains("3 dự án", message);
            Assert.Contains("nâng cấp", message);
        }

        [Fact]
        public async Task ArchivingAProject_FreesASlot_ButRestoringWhenFullIsBlocked()
        {
            using var factory = new ApiFactory();
            var (client, _) = await LoginAsync(factory);
            var first = await NewProjectAsync(client, "Dự án 1");
            await NewProjectAsync(client, "Dự án 2");
            await NewProjectAsync(client, "Dự án 3");

            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/projects/{first}/archive", null)).StatusCode);
            await NewProjectAsync(client, "Dự án 4");   // chỗ trống vừa có

            // Đang đủ 3 dự án hoạt động: khôi phục dự án đã lưu trữ cũng bị chặn
            var restore = await client.PostAsync($"/api/projects/{first}/restore", null);
            Assert.Equal(HttpStatusCode.BadRequest, restore.StatusCode);

            // Còn là Archived (không bị đổi sang Active)
            var project = await client.GetFromJsonAsync<ProjectDto>($"/api/projects/{first}");
            Assert.Equal("Archived", project!.Status);
        }

        [Fact]
        public async Task RestoringAnActiveProject_IsNotCountedAgainstTheLimit()
        {
            using var factory = new ApiFactory();
            var (client, _) = await LoginAsync(factory);
            var ids = new List<Guid>();
            for (var i = 1; i <= 3; i++) ids.Add(await NewProjectAsync(client, $"Dự án {i}"));

            // Đang đủ hạn mức nhưng dự án này đã Active: gọi khôi phục không làm tăng số dự án nên không bị chặn
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/projects/{ids[0]}/restore", null)).StatusCode);
        }

        [Fact]
        public async Task NullProjectLimit_MeansUnlimited_AndAHigherPlanAllowsMore()
        {
            using var factory = new ApiFactory();
            var (client, userId) = await LoginAsync(factory);

            await UsePlanAsync(factory, userId, projectLimit: null, storageMb: 1024);
            for (var i = 1; i <= 6; i++) await NewProjectAsync(client, $"Không giới hạn {i}");

            await UsePlanAsync(factory, userId, projectLimit: 7, storageMb: 1024);   // đang có 6
            await NewProjectAsync(client, "Dự án thứ 7");
            Assert.Equal(HttpStatusCode.BadRequest, (await CreateProjectAsync(client, "Dự án thứ 8")).StatusCode);
        }

        [Fact]
        public async Task LimitsAreCountedPerUser()
        {
            using var factory = new ApiFactory();
            var (a, _) = await LoginAsync(factory, "user-a");
            var (b, _) = await LoginAsync(factory, "user-b");
            for (var i = 1; i <= 3; i++) await NewProjectAsync(a, $"A{i}");

            Assert.Equal(HttpStatusCode.BadRequest, (await CreateProjectAsync(a, "A4")).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await CreateProjectAsync(b, "B1")).StatusCode);
        }

        [Fact]
        public async Task DowngradingWhileOverTheLimit_KeepsEverything_ButBlocksNewProjects()
        {
            using var factory = new ApiFactory();
            var (client, userId) = await LoginAsync(factory);
            await UsePlanAsync(factory, userId, projectLimit: null, storageMb: 1024);
            for (var i = 1; i <= 5; i++) await NewProjectAsync(client, $"Dự án {i}");

            // Hết kỳ, về gói giới hạn 3 dự án khi đang có 5
            await UsePlanAsync(factory, userId, projectLimit: 3, storageMb: 100);

            var projects = await client.GetFromJsonAsync<List<ProjectWithClientDto>>("/api/projects");
            Assert.True(projects!.Count >= 5);   // không mất dự án nào (chưa kể dự án mặc định)
            Assert.Equal(HttpStatusCode.BadRequest, (await CreateProjectAsync(client, "Dự án 6")).StatusCode);
        }

        [Fact]
        public async Task WithoutAnActiveSubscription_NothingIsBlocked()
        {
            using var factory = new ApiFactory();
            var (client, userId) = await LoginAsync(factory);
            await factory.SeedAsync(db => db.UserSubscriptions.Single(s => s.UserId == userId && s.Status == "Active").Status = "Expired");

            for (var i = 1; i <= 5; i++) await NewProjectAsync(client, $"Dự án {i}");
        }

        // ------------------------------------------------------------ Dung lượng file

        [Fact]
        public async Task Storage_AllowsUpToTheExactLimit_ThenBlocks_AndLeavesNoFileBehind()
        {
            using var factory = new ApiFactory();
            var (client, userId) = await LoginAsync(factory);
            await UsePlanAsync(factory, userId, projectLimit: null, storageMb: 1);   // 1 MB
            var projectId = await NewProjectAsync(client, "Có file");

            Assert.Equal(HttpStatusCode.Created, (await UploadAsync(client, projectId, Mb / 2)).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await UploadAsync(client, projectId, Mb / 2)).StatusCode);   // bằng đúng 1 MB: cho
            var filesBefore = StoredFiles(factory);

            var over = await UploadAsync(client, projectId, 1);   // thêm 1 byte: vượt

            Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
            var message = await ErrorAsync(over);
            Assert.Contains("1 MB", message);
            Assert.Contains("nâng cấp", message);
            Assert.Equal(filesBefore, StoredFiles(factory));   // bị chặn thì không ghi file xuống kho
        }

        [Fact]
        public async Task Storage_CountsAcrossAllProjects_IgnoresLinks_AndDeletingFreesSpace()
        {
            using var factory = new ApiFactory();
            var (client, userId) = await LoginAsync(factory);
            await UsePlanAsync(factory, userId, projectLimit: null, storageMb: 1);
            var p1 = await NewProjectAsync(client, "P1");
            var p2 = await NewProjectAsync(client, "P2");

            var big = await UploadAsync(client, p1, Mb * 3 / 4);
            Assert.Equal(HttpStatusCode.Created, big.StatusCode);
            var doc = (await big.Content.ReadFromJsonAsync<ProjectDocumentDto>())!;

            // Dự án khác của cùng người dùng chia chung hạn mức
            Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(client, p2, Mb / 2)).StatusCode);

            // Link không tính dung lượng
            var link = await client.PostAsJsonAsync($"/api/projects/{p2}/documents/links",
                new ProjectDocumentRequestDto { Name = "Tài liệu", LinkUrl = "https://example.com/a" });
            Assert.Equal(HttpStatusCode.Created, link.StatusCode);

            // Xóa file lớn thì có chỗ lại
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/documents/{doc.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await UploadAsync(client, p2, Mb / 2)).StatusCode);
        }

        // ------------------------------------------------------------ GET /api/me/usage

        [Fact]
        public async Task Usage_ReportsProjectsAndStorageAgainstThePlanLimits()
        {
            using var factory = new ApiFactory();
            var (client, userId) = await LoginAsync(factory);
            await UsePlanAsync(factory, userId, projectLimit: 5, storageMb: 10, name: "Pro");
            var p1 = await NewProjectAsync(client, "P1");
            var archived = await NewProjectAsync(client, "P2");
            await client.PostAsync($"/api/projects/{archived}/archive", null);
            await UploadAsync(client, p1, 2048);

            var usage = (await client.GetFromJsonAsync<PlanUsageDto>("/api/me/usage"))!;

            Assert.StartsWith("Pro", usage.PlanName);
            Assert.Equal(1, usage.ProjectsUsed);        // không tính dự án lưu trữ và dự án mặc định
            Assert.Equal(5, usage.ProjectLimit);
            Assert.Equal(2048, usage.StorageUsedBytes);
            Assert.Equal(10, usage.StorageLimitMb);
        }

        [Fact]
        public async Task Usage_AndMe_ShowWhenThePlanStartedAndHowManyDaysAreLeft()
        {
            using var factory = new ApiFactory();
            var (client, userId) = await LoginAsync(factory);
            await UsePlanAsync(factory, userId, projectLimit: 5, storageMb: 512, name: "Pro");
            var start = DateTime.UtcNow.AddDays(-20);
            var end = DateTime.UtcNow.AddDays(10).AddHours(-3);   // còn 9 ngày 21 giờ: làm tròn lên thành 10
            await factory.SeedAsync(db =>
            {
                var sub = db.UserSubscriptions.Single(x => x.UserId == userId && x.Status == "Active");
                sub.StartAt = start;
                sub.EndAt = end;
            });

            var usage = (await client.GetFromJsonAsync<PlanUsageDto>("/api/me/usage"))!;
            var me = (await client.GetFromJsonAsync<MeDto>("/api/me"))!;

            Assert.Equal(start, usage.SubscriptionStartAt!.Value.UtcDateTime, TimeSpan.FromSeconds(1));
            Assert.Equal(end, usage.SubscriptionEndAt!.Value.UtcDateTime, TimeSpan.FromSeconds(1));
            Assert.Equal(10, usage.RemainingDays);
            Assert.Equal(start, me.CurrentSubscription!.StartAt.UtcDateTime, TimeSpan.FromSeconds(1));
            Assert.Equal(10, me.CurrentSubscription.RemainingDays);
        }

        [Fact]
        public async Task FreePlan_HasNoEndDate_SoNoRemainingDays()
        {
            using var factory = new ApiFactory();
            var (client, _) = await LoginAsync(factory);

            var usage = (await client.GetFromJsonAsync<PlanUsageDto>("/api/me/usage"))!;

            Assert.NotNull(usage.SubscriptionStartAt);
            Assert.Null(usage.SubscriptionEndAt);
            Assert.Null(usage.RemainingDays);
        }

        [Fact]
        public async Task Usage_RequiresLogin()
        {
            using var factory = new ApiFactory();

            Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/me/usage")).StatusCode);
        }
    }
}
