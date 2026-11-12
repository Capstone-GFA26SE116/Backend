using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Infrastructure.Data;
using METANOIA.Test.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DomainEntities = METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // Đồng bộ lại trạng thái cho dữ liệu cũ bị lệch (POST /api/admin/status-resync, xem docs/STATUS_WORKFLOW.md mục 9)
    public class StatusResyncTests
    {
        private sealed record Lagging(Guid Scheduled, Guid FinishedSubTasks, Guid ClosedWithOpenSub, Guid Milestone, Guid Untouched);

        private static async Task<(HttpClient Admin, HttpClient User, Lagging Ids)> SetupAsync(ApiFactory factory)
        {
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, _) = await TestHelpers.LoginAsync(factory, "admin-1");

            var domainId = Guid.NewGuid();
            await factory.SeedAsync(db => db.Domains.Add(new DomainEntities.Domain { Id = domainId, Name = $"Dev-{Guid.NewGuid()}", IsActive = true }));
            var (user, _) = await TestHelpers.LoginAsync(factory, "user-1");
            var project = (await (await user.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Cũ" })).Content.ReadFromJsonAsync<ProjectDto>())!;

            async Task<Guid> NewTask(string title, Guid? milestoneId = null)
            {
                var r = await user.PostAsJsonAsync($"/api/projects/{project.Id}/tasks", new TaskRequestDto
                {
                    Title = title, TaskType = "Flexible", DomainId = domainId, MilestoneId = milestoneId,
                    Deadline = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), EstimatedMinutes = 60
                });
                Assert.Equal(HttpStatusCode.Created, r.StatusCode);
                return (await r.Content.ReadFromJsonAsync<TaskDto>())!.Id;
            }

            async Task<Guid> NewSub(Guid taskId, int order)
            {
                var r = await user.PostAsJsonAsync($"/api/tasks/{taskId}/subtasks", new SubTaskRequestDto { Title = $"S{order}", EstimatedMinutes = 10, OrderIndex = order });
                Assert.Equal(HttpStatusCode.Created, r.StatusCode);
                return (await r.Content.ReadFromJsonAsync<SubTaskDto>())!.Id;
            }

            var milestone = (await (await user.PostAsJsonAsync($"/api/projects/{project.Id}/milestones", new MilestoneRequestDto
            { Name = "GĐ", OrderIndex = 1, DueDate = new DateOnly(2030, 1, 1), Status = "Planned" })).Content.ReadFromJsonAsync<MilestoneDto>())!;

            // 1) có slot Approved (qua API nên task đã Scheduled), sau đó dữ liệu "cũ" bị lệch về ToDo
            var scheduled = await NewTask("Có slot");
            var start = factory.Clock.GetUtcNow().AddHours(2);
            var slot = await user.PostAsJsonAsync($"/api/tasks/{scheduled}/schedule-slots", new ScheduleSlotRequestDto
            { StartTime = start, EndTime = start.AddMinutes(30), IsManualOverride = true, Status = "Approved" });
            Assert.Equal(HttpStatusCode.Created, slot.StatusCode);

            // 2) mọi subtask đã Done nhưng task vẫn InProgress
            var finished = await NewTask("Subtask xong");
            var f1 = await NewSub(finished, 1);
            var f2 = await NewSub(finished, 2);

            // 3) task đã Done (người dùng chốt, kiểu cũ) nhưng còn subtask ToDo: KHÔNG được đụng tới
            var closed = await NewTask("Đã đóng kiểu cũ", milestone.Id);
            var c1 = await NewSub(closed, 1);

            // 4) task thường, không lệch gì
            var untouched = await NewTask("Bình thường");

            await factory.SeedAsync(db =>
            {
                db.Tasks.Single(t => t.Id == scheduled).Status = "ToDo";
                db.Tasks.Single(t => t.Id == finished).Status = "InProgress";
                foreach (var id in new[] { f1, f2 }) db.SubTasks.Single(s => s.Id == id).Status = "Done";
                var closedTask = db.Tasks.Single(t => t.Id == closed);
                closedTask.Status = "Done";
                closedTask.CompletedAt = DateTime.UtcNow;
            });

            return (admin, user, new Lagging(scheduled, finished, closed, milestone.Id, untouched));
        }

        private static async Task<(string Task, string Milestone, string ClosedSub)> StatesAsync(ApiFactory factory, Lagging ids, Guid probeTask)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = (await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == probeTask)).Status;
            var milestone = (await db.Milestones.AsNoTracking().SingleAsync(m => m.Id == ids.Milestone)).Status;
            var sub = (await db.SubTasks.AsNoTracking().SingleAsync(s => s.TaskId == ids.ClosedWithOpenSub)).Status;
            return (task, milestone, sub);
        }

        [Fact]
        public async Task Resync_IsForbiddenForNormalUsers_AndUnauthorizedWithoutLogin()
        {
            using var factory = new ApiFactory();
            var (_, user, _) = await SetupAsync(factory);

            Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/admin/status-resync", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync("/api/admin/status-resync", null)).StatusCode);
        }

        [Fact]
        public async Task Resync_DryRunIsTheDefault_ReportsChangesWithoutSavingAnything()
        {
            using var factory = new ApiFactory();
            var (admin, _, ids) = await SetupAsync(factory);

            var response = await admin.PostAsync("/api/admin/status-resync", null);   // không truyền dryRun → chạy thử
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var report = (await response.Content.ReadFromJsonAsync<StatusResyncReportDto>())!;

            Assert.True(report.DryRun);
            Assert.Contains(report.Changes, c => c.Id == ids.Scheduled && c.From == "ToDo" && c.To == "Scheduled");
            Assert.Contains(report.Changes, c => c.Id == ids.FinishedSubTasks && c.From == "InProgress" && c.To == "Done");
            Assert.Contains(report.Changes, c => c.Id == ids.Milestone && c.Kind == "Milestone" && c.From == "Planned" && c.To == "InProgress");

            // Chưa lưu gì
            Assert.Equal("ToDo", (await StatesAsync(factory, ids, ids.Scheduled)).Task);
            Assert.Equal("InProgress", (await StatesAsync(factory, ids, ids.FinishedSubTasks)).Task);
            Assert.Equal("Planned", (await StatesAsync(factory, ids, ids.Scheduled)).Milestone);
        }

        [Fact]
        public async Task Resync_Apply_FixesLaggingData_LeavesClosedTasksAlone_AndSecondRunIsClean()
        {
            using var factory = new ApiFactory();
            var (admin, _, ids) = await SetupAsync(factory);

            var report = (await (await admin.PostAsync("/api/admin/status-resync?dryRun=false", null)).Content.ReadFromJsonAsync<StatusResyncReportDto>())!;

            Assert.False(report.DryRun);
            Assert.Equal("Scheduled", (await StatesAsync(factory, ids, ids.Scheduled)).Task);
            Assert.Equal("Done", (await StatesAsync(factory, ids, ids.FinishedSubTasks)).Task);
            Assert.Equal("InProgress", (await StatesAsync(factory, ids, ids.Untouched)).Milestone);   // giai đoạn đã có task Done

            // Task Done kiểu cũ còn subtask ToDo: giữ nguyên (không mở lại, không đổi subtask)
            var (closed, _, closedSub) = await StatesAsync(factory, ids, ids.ClosedWithOpenSub);
            Assert.Equal("Done", closed);
            Assert.Equal("ToDo", closedSub);

            // Task bình thường không bị liệt kê
            Assert.DoesNotContain(report.Changes, c => c.Id == ids.Untouched);

            // Chạy lại: không còn gì để sửa
            var second = (await (await admin.PostAsync("/api/admin/status-resync?dryRun=false", null)).Content.ReadFromJsonAsync<StatusResyncReportDto>())!;
            Assert.Equal(0, second.TasksChanged);
            Assert.Equal(0, second.MilestonesChanged);
            Assert.Empty(second.Changes);
        }
    }
}
