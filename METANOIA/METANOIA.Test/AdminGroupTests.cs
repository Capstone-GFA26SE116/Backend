using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using METANOIA.Application.Dto;
using METANOIA.Infrastructure.Data;
using METANOIA.Test.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // Admin quản trị nhóm (nhánh Group): xem, thống kê, lưu trữ, khôi phục (có lý do, có nhật ký)
    public class AdminGroupTests
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private sealed record Ctx(HttpClient Admin, Guid AdminId, HttpClient Leader, Guid LeaderId, HttpClient Member, Guid MemberId, GroupDto Group);

        private static async Task<Ctx> SetupAsync(ApiFactory factory)
        {
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, adminAuth) = await TestHelpers.LoginAsync(factory, "admin-1");
            var (leader, leaderAuth) = await TestHelpers.LoginAsync(factory, "leader");
            var (member, memberAuth) = await TestHelpers.LoginAsync(factory, "member");
            // Người dùng thường không gửi lý do của Admin
            leader.DefaultRequestHeaders.Remove("X-Admin-Reason");
            member.DefaultRequestHeaders.Remove("X-Admin-Reason");

            var created = await leader.PostAsJsonAsync("/api/groups", new GroupRequestDto { Name = "Nhóm đồ án", Description = "Mô tả" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var group = (await created.Content.ReadFromJsonAsync<GroupDto>())!;

            await leader.PostAsJsonAsync($"/api/groups/{group.Id}/invitations", new GroupInvitationRequestDto { Email = memberAuth.Email, Role = "Member" });
            var mine = await member.GetFromJsonAsync<List<GroupInvitationDto>>("/api/group-invitations");
            var accepted = await member.PostAsync($"/api/group-invitations/{mine![0].Id}/accept", null);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

            var task = await leader.PostAsJsonAsync($"/api/groups/{group.Id}/tasks",
                new GroupTaskRequestDto { Title = "Việc bí mật của nhóm", AssigneeId = memberAuth.UserId });
            Assert.Equal(HttpStatusCode.Created, task.StatusCode);

            return new Ctx(admin, adminAuth.UserId, leader, leaderAuth.UserId, member, memberAuth.UserId, group);
        }

        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static async Task<string> MessageAsync(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("message").GetString()!;
        }

        [Fact]
        public async Task AdminGroups_AreForbiddenForUsers_AndUnauthorizedWithoutLogin()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);

            foreach (var url in new[] { "/api/admin/groups", "/api/admin/groups/stats", $"/api/admin/groups/{c.Group.Id}", $"/api/admin/groups/{c.Group.Id}/activity" })
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await c.Leader.GetAsync(url)).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
            }

            Assert.Equal(HttpStatusCode.Forbidden, (await c.Leader.PostAsync($"/api/admin/groups/{c.Group.Id}/archive", null)).StatusCode);
        }

        [Fact]
        public async Task Search_ListsGroupsWithOwnerAndCounts_AndFilters()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);
            await c.Member.PostAsJsonAsync("/api/groups", new GroupRequestDto { Name = "Nhóm khác" });

            var all = (await c.Admin.GetFromJsonAsync<PagedResultDto<AdminGroupListItemDto>>("/api/admin/groups", Json))!;
            Assert.Equal(2, all.TotalCount);

            var item = Assert.Single(all.Items, g => g.Id == c.Group.Id);
            Assert.Equal(c.LeaderId, item.OwnerId);
            Assert.Equal(2, item.MemberCount);
            Assert.Equal(1, item.TaskCount);
            Assert.Equal("Active", item.Status);

            var byName = (await c.Admin.GetFromJsonAsync<PagedResultDto<AdminGroupListItemDto>>("/api/admin/groups?search=%C4%91%E1%BB%93%20%C3%A1n", Json))!;
            Assert.Single(byName.Items);

            var byOwner = (await c.Admin.GetFromJsonAsync<PagedResultDto<AdminGroupListItemDto>>($"/api/admin/groups?ownerId={c.MemberId}", Json))!;
            Assert.Equal("Nhóm khác", Assert.Single(byOwner.Items).Name);

            var page = (await c.Admin.GetFromJsonAsync<PagedResultDto<AdminGroupListItemDto>>("/api/admin/groups?page=2&pageSize=1", Json))!;
            Assert.Single(page.Items);
            Assert.Equal(2, page.TotalCount);

            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.GetAsync("/api/admin/groups?status=Lạ")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.GetAsync("/api/admin/groups?pageSize=500")).StatusCode);
        }

        [Fact]
        public async Task Get_ReturnsMembersAndProgress_ButNoTaskTitlesOrComments()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);

            var response = await c.Admin.GetAsync($"/api/admin/groups/{c.Group.Id}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var raw = await response.Content.ReadAsStringAsync();
            var detail = JsonSerializer.Deserialize<AdminGroupDetailDto>(raw, Json)!;

            Assert.Equal(2, detail.Members.Count);
            Assert.Equal(1, detail.Progress.TotalTasks);
            Assert.Equal(c.LeaderId, detail.OwnerId);
            Assert.DoesNotContain("Việc bí mật", raw);

            Assert.Equal(HttpStatusCode.NotFound, (await c.Admin.GetAsync($"/api/admin/groups/{Guid.NewGuid()}")).StatusCode);
        }

        [Fact]
        public async Task Activity_ListsGroupHistory_NewestFirst()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);

            var list = (await c.Admin.GetFromJsonAsync<List<GroupActivityDto>>($"/api/admin/groups/{c.Group.Id}/activity", Json))!;

            Assert.NotEmpty(list);
            Assert.Contains(list, a => a.Action == "Created" && a.EntityType == "Group");
            Assert.Equal(list.OrderByDescending(a => a.CreatedAt).Select(a => a.Id), list.Select(a => a.Id));
            Assert.Equal(HttpStatusCode.NotFound, (await c.Admin.GetAsync($"/api/admin/groups/{Guid.NewGuid()}/activity")).StatusCode);
        }

        [Fact]
        public async Task Stats_CountsGroupsMembersAndTasks()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);
            await c.Member.PostAsJsonAsync("/api/groups", new GroupRequestDto { Name = "Nhóm hai" });
            await c.Admin.PostAsync($"/api/admin/groups/{c.Group.Id}/archive", null);

            var stats = (await c.Admin.GetFromJsonAsync<AdminGroupStatsDto>("/api/admin/groups/stats", Json))!;

            Assert.Equal(2, stats.TotalGroups);
            Assert.Equal(1, stats.ActiveGroups);
            Assert.Equal(1, stats.ArchivedGroups);
            Assert.Equal(3, stats.TotalMemberships);
            Assert.Equal(2, stats.DistinctMembers);
            Assert.Equal(1, stats.TotalTasks);
            Assert.Equal(1, stats.TasksByStatus["ToDo"]);
            Assert.Equal(0, stats.TasksByStatus["Done"]);
            Assert.Equal(2, stats.GroupsCreatedLast30Days);
            Assert.Equal(0, stats.RunningFocusSessions);
        }

        [Fact]
        public async Task Archive_WithoutReason_Returns400_AndChangesNothing()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);
            c.Admin.DefaultRequestHeaders.Remove("X-Admin-Reason");

            var response = await c.Admin.PostAsync($"/api/admin/groups/{c.Group.Id}/archive", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("lý do", await MessageAsync(response));
            Assert.Equal("Active", (await QueryAsync(factory, db => db.WorkGroups.AsNoTracking().SingleAsync(g => g.Id == c.Group.Id))).Status);

            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.PostAsync($"/api/admin/groups/{c.Group.Id}/restore", null)).StatusCode);
        }

        [Fact]
        public async Task Archive_ThenRestore_ChangesStatus_AndWritesAdminLog_GroupLog_AndNotifiesLeader()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);

            var archived = await c.Admin.PostAsync($"/api/admin/groups/{c.Group.Id}/archive", null);
            Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
            var detail = (await archived.Content.ReadFromJsonAsync<AdminGroupDetailDto>(Json))!;
            Assert.Equal("Archived", detail.Status);

            // Thành viên thấy nhóm đã lưu trữ và không còn sửa được
            var mine = (await c.Leader.GetFromJsonAsync<GroupDto>($"/api/groups/{c.Group.Id}", Json))!;
            Assert.Equal("Archived", mine.Status);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await c.Leader.PutAsJsonAsync($"/api/groups/{c.Group.Id}", new GroupRequestDto { Name = "Đổi tên" })).StatusCode);

            // AdminActionLog: ghi theo bảng nhóm, có lý do và giá trị cũ/mới
            var adminLog = Assert.Single(await QueryAsync(factory, db => db.AdminActionLogs.AsNoTracking().Where(l => l.TargetType == "WorkGroup").ToListAsync()));
            Assert.Equal("WorkGroup.Update", adminLog.Action);
            Assert.Equal(c.Group.Id, adminLog.TargetId);
            Assert.Equal(c.AdminId, adminLog.AdminId);
            Assert.Equal("ly-do-kiem-thu", adminLog.Reason);
            Assert.Contains("Archived", adminLog.NewValue);

            // Nhật ký của nhóm có dòng do Admin
            var activity = (await c.Leader.GetFromJsonAsync<List<GroupActivityDto>>($"/api/groups/{c.Group.Id}/activity", Json))!;
            var entry = Assert.Single(activity, a => a.Action == "AdminArchived");
            Assert.Equal(c.AdminId, entry.ActorId);
            Assert.Contains("ly-do-kiem-thu", entry.NewValue);

            // Leader (không phải Member) nhận thông báo kèm liên kết tới nhóm
            var notifications = await QueryAsync(factory, db => db.Notifications.AsNoTracking().Where(n => n.GroupId == c.Group.Id && n.Type == "GroupStatusChanged").ToListAsync());
            var notification = Assert.Single(notifications);
            Assert.Equal(c.LeaderId, notification.UserId);
            Assert.Contains("ly-do-kiem-thu", notification.Message);

            // Khôi phục
            var restored = await c.Admin.PostAsync($"/api/admin/groups/{c.Group.Id}/restore", null);
            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
            Assert.Equal("Active", (await restored.Content.ReadFromJsonAsync<AdminGroupDetailDto>(Json))!.Status);
            Assert.Equal(2, (await QueryAsync(factory, db => db.AdminActionLogs.AsNoTracking().CountAsync(l => l.TargetType == "WorkGroup"))));

            var after = (await c.Leader.GetFromJsonAsync<List<GroupActivityDto>>($"/api/groups/{c.Group.Id}/activity", Json))!;
            Assert.Contains(after, a => a.Action == "AdminRestored");
        }

        [Fact]
        public async Task Archive_AlreadyArchived_AndRestore_AlreadyActive_Return400()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);

            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.PostAsync($"/api/admin/groups/{c.Group.Id}/restore", null)).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await c.Admin.PostAsync($"/api/admin/groups/{c.Group.Id}/archive", null)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await c.Admin.PostAsync($"/api/admin/groups/{c.Group.Id}/archive", null)).StatusCode);

            Assert.Equal(HttpStatusCode.NotFound, (await c.Admin.PostAsync($"/api/admin/groups/{Guid.NewGuid()}/archive", null)).StatusCode);
        }

        [Fact]
        public async Task Admin_CanArchiveGroupWithoutBeingMember_AndLeaderCanStillRestoreItNormally()
        {
            using var factory = new ApiFactory();
            var c = await SetupAsync(factory);
            await c.Admin.PostAsync($"/api/admin/groups/{c.Group.Id}/archive", null);

            // Leader khôi phục bằng API thường của nhóm: luật nhóm không đổi
            var restored = await c.Leader.PostAsync($"/api/groups/{c.Group.Id}/restore", null);

            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
            // Thao tác của Leader (không phải Admin) không ghi AdminActionLog: vẫn chỉ có dòng lưu trữ của Admin
            Assert.Single(await QueryAsync(factory, db => db.AdminActionLogs.AsNoTracking().Where(l => l.TargetType == "WorkGroup").ToListAsync()));
        }
    }
}
