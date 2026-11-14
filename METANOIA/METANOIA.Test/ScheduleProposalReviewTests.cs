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
    // FR-11: sửa, từ chối từng slot đề xuất và kiểm lại Google Calendar khi duyệt
    public class ScheduleProposalReviewTests
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private sealed record Ctx(HttpClient Client, Guid UserId, Guid TaskId, Guid ProposalId, Guid[] SlotIds, DateTime Day);

        private static ApiFactory NewFactory() => new() { FakeCalendarApi = true };

        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        // Đề xuất Pending với `slots` slot Proposed, mỗi slot 60 phút bắt đầu 03:00, 05:00, 07:00 UTC của một ngày trong tương lai
        private static async Task<Ctx> SeedAsync(ApiFactory factory, int slots = 2, string proposalStatus = "Pending")
        {
            var domainId = Guid.NewGuid();
            await factory.SeedAsync(db => db.Domains.Add(new METANOIA.Domain.Entities.Domain { Id = domainId, Name = $"Dev-{Guid.NewGuid()}", IsActive = true }));
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            var project = (await (await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Website" })).Content.ReadFromJsonAsync<ProjectDto>())!;
            var task = (await (await client.PostAsJsonAsync($"/api/projects/{project.Id}/tasks", new TaskRequestDto
            {
                Title = "Viết báo cáo", TaskType = "Flexible", DomainId = domainId,
                Deadline = new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero), EstimatedMinutes = 120
            })).Content.ReadFromJsonAsync<TaskDto>())!;

            var day = factory.Clock.GetUtcNow().UtcDateTime.Date.AddDays(10);
            var proposalId = Guid.NewGuid();
            var slotIds = Enumerable.Range(0, slots).Select(_ => Guid.NewGuid()).ToArray();
            await factory.SeedAsync(db =>
            {
                db.ScheduleProposals.Add(new ScheduleProposal
                {
                    Id = proposalId, UserId = auth.UserId, Level = 3, Status = proposalStatus, IsFeasible = true,
                    Explanation = "Đề xuất thử", CreatedAt = DateTime.UtcNow
                });
                for (var i = 0; i < slotIds.Length; i++)
                {
                    var start = day.AddHours(3 + i * 2);
                    db.ScheduleSlots.Add(new ScheduleSlot
                    {
                        Id = slotIds[i], TaskId = task.Id, ScheduleProposalId = proposalId, StartTime = start,
                        EndTime = start.AddMinutes(60), ProposedMinutes = 60, Status = "Proposed"
                    });
                }
            });
            return new Ctx(client, auth.UserId, task.Id, proposalId, slotIds, day);
        }

        private static async Task<Guid> ConnectGoogleAsync(ApiFactory factory, Guid userId)
        {
            var id = Guid.NewGuid();
            await factory.SeedAsync(db => db.GoogleConnections.Add(new GoogleConnection
            {
                Id = id, UserId = userId, Status = "Active", AccessTokenEncrypted = new byte[] { 1 }, RefreshTokenEncrypted = new byte[] { 1 },
                TokenExpiresAt = DateTime.UtcNow.AddHours(1), CreatedAt = DateTime.UtcNow
            }));
            return id;
        }

        private static async Task CacheEventAsync(ApiFactory factory, Guid connectionId, string eventId, DateTime start, DateTime end,
            bool allDay = false, bool cancelled = false)
        {
            await factory.SeedAsync(db => db.GoogleCalendarEvents.Add(new GoogleCalendarEvent
            {
                Id = Guid.NewGuid(), GoogleConnectionId = connectionId, GoogleEventId = eventId, Title = "Họp khách", StartTime = start, EndTime = end,
                IsAllDay = allDay, IsCreatedByMetanoia = eventId.StartsWith("metanoia"), Etag = "e", IsCancelled = cancelled, UpdatedAt = DateTime.UtcNow
            }));
        }

        private static string Url(Ctx c, int slot = 0) => $"/api/schedule/proposals/{c.ProposalId}/slots/{c.SlotIds[slot]}";

        private static ProposedSlotUpdateRequest Times(DateTime start, int minutes = 60) =>
            new() { StartTime = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)), EndTime = new DateTimeOffset(DateTime.SpecifyKind(start.AddMinutes(minutes), DateTimeKind.Utc)) };

        private static async Task<ScheduleSlot> SlotAsync(ApiFactory f, Guid id) => await QueryAsync(f, db => db.ScheduleSlots.AsNoTracking().SingleAsync(s => s.Id == id));

        private static async Task<List<ScheduleChangeLog>> LogsAsync(ApiFactory f, Guid slotId) =>
            await QueryAsync(f, db => db.ScheduleChangeLogs.AsNoTracking().Where(l => l.ScheduleSlotId == slotId).OrderBy(l => l.ChangedAt).ToListAsync());

        private static async Task<JsonElement> BodyAsync(HttpResponseMessage r)
        {
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        // ------------------------------------------------------------ Sửa một slot

        [Fact]
        public async Task Edit_ChangesTheTimes_RecomputesMinutes_KeepsTheOriginalStart_AndLogsMovedThenResized()
        {
            using var factory = NewFactory();
            var c = await SeedAsync(factory);
            var originalStart = (await SlotAsync(factory, c.SlotIds[0])).StartTime;

            var moved = await c.Client.PutAsJsonAsync(Url(c), Times(c.Day.AddHours(10), 90));
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
            var result = (await moved.Content.ReadFromJsonAsync<ProposedSlotUpdateResultDto>(Json))!;
            Assert.Equal("Proposed", result.Slot.Status);
            Assert.Empty(result.GoogleConflicts);

            var slot = await SlotAsync(factory, c.SlotIds[0]);
            Assert.Equal(c.Day.AddHours(10), slot.StartTime);
            Assert.Equal(90, slot.ProposedMinutes);
            Assert.True(slot.IsManualOverride);
            Assert.Equal(originalStart, slot.OriginalStartTime);

            // Chỉ đổi giờ kết thúc: Resized, OriginalStartTime không bị ghi đè
            await c.Client.PutAsJsonAsync(Url(c), Times(c.Day.AddHours(10), 120));
            var logs = await LogsAsync(factory, c.SlotIds[0]);
            Assert.Equal(new[] { "Moved", "Resized" }, logs.Select(l => l.ChangeType).ToArray());
            Assert.All(logs, l => Assert.Equal("Manual", l.Source));
            Assert.Equal(originalStart, (await SlotAsync(factory, c.SlotIds[0])).OriginalStartTime);

            // Gửi lại đúng giờ cũ: không ghi thêm log
            await c.Client.PutAsJsonAsync(Url(c), Times(c.Day.AddHours(10), 120));
            Assert.Equal(2, (await LogsAsync(factory, c.SlotIds[0])).Count);
        }

        [Fact]
        public async Task Edit_DoesNotApproveAnything_NoJobs_AndTheTaskStaysToDo()
        {
            using var factory = NewFactory();
            var c = await SeedAsync(factory);

            await c.Client.PutAsJsonAsync(Url(c), Times(c.Day.AddHours(12)));

            Assert.Empty(factory.Jobs.Calls);
            Assert.Equal("ToDo", (await c.Client.GetFromJsonAsync<TaskDto>($"/api/tasks/{c.TaskId}"))!.Status);
            Assert.Equal("Pending", (await QueryAsync(factory, db => db.ScheduleProposals.AsNoTracking().SingleAsync(p => p.Id == c.ProposalId))).Status);
        }

        [Fact]
        public async Task Edit_IsRejected_WhenTheInputOrStateIsInvalid()
        {
            using var factory = NewFactory();
            var c = await SeedAsync(factory);

            // Kết thúc không sau bắt đầu, và bắt đầu ở quá khứ
            Assert.Equal(HttpStatusCode.BadRequest, (await c.Client.PutAsJsonAsync(Url(c), new ProposedSlotUpdateRequest
            { StartTime = new DateTimeOffset(c.Day.AddHours(5), TimeSpan.Zero), EndTime = new DateTimeOffset(c.Day.AddHours(5), TimeSpan.Zero) })).StatusCode);
            var past = await c.Client.PutAsJsonAsync(Url(c), Times(factory.Clock.GetUtcNow().UtcDateTime.AddHours(-3)));
            Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);

            // Slot không còn Proposed
            await c.Client.PostAsync($"{Url(c, 1)}/reject", null);
            Assert.Equal(HttpStatusCode.BadRequest, (await c.Client.PutAsJsonAsync(Url(c, 1), Times(c.Day.AddHours(14)))).StatusCode);

            // Slot không thuộc đề xuất này, và người khác
            var elsewhere = $"/api/schedule/proposals/{c.ProposalId}/slots/{Guid.NewGuid()}";
            Assert.Equal(HttpStatusCode.NotFound, (await c.Client.PutAsJsonAsync(elsewhere, Times(c.Day.AddHours(14)))).StatusCode);
            var (stranger, _) = await TestHelpers.LoginAsync(factory, "stranger");
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync(Url(c), Times(c.Day.AddHours(14)))).StatusCode);
        }

        [Fact]
        public async Task Edit_AndReject_AreRejected_WhenTheProposalIsNoLongerPending()
        {
            using var factory = NewFactory();
            var c = await SeedAsync(factory, proposalStatus: "Superseded");

            var edit = await c.Client.PutAsJsonAsync(Url(c), Times(c.Day.AddHours(12)));
            var reject = await c.Client.PostAsync($"{Url(c)}/reject", null);
            var approve = await c.Client.PostAsync($"/api/schedule/proposals/{c.ProposalId}/approve", null);

            Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, reject.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);
            Assert.Contains("Superseded", await edit.Content.ReadAsStringAsync());
            Assert.Equal("Proposed", (await SlotAsync(factory, c.SlotIds[0])).Status);
        }

        // ------------------------------------------------------------ Từ chối một slot

        [Fact]
        public async Task Reject_CancelsTheSlot_RemovesItFromPending_LogsDeleted_AndApprovalSkipsIt()
        {
            using var factory = NewFactory();
            var c = await SeedAsync(factory);

            var reject = await c.Client.PostAsync($"{Url(c, 0)}/reject", null);
            Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
            Assert.Equal("Cancelled", (await reject.Content.ReadFromJsonAsync<ScheduleSlotDto>(Json))!.Status);

            var log = Assert.Single(await LogsAsync(factory, c.SlotIds[0]));
            Assert.Equal(("Deleted", "Manual"), (log.ChangeType, log.Source));

            var pending = (await c.Client.GetFromJsonAsync<ScheduleProposalDto>("/api/schedule/proposals/pending", Json))!;
            Assert.Equal(c.SlotIds[1], Assert.Single(pending.Slots).Id);
            Assert.Equal("ToDo", (await c.Client.GetFromJsonAsync<TaskDto>($"/api/tasks/{c.TaskId}"))!.Status);

            // Từ chối lần hai: 400
            Assert.Equal(HttpStatusCode.BadRequest, (await c.Client.PostAsync($"{Url(c, 0)}/reject", null)).StatusCode);

            // Duyệt: chỉ slot còn lại được duyệt, slot đã từ chối vẫn Cancelled
            var approve = await c.Client.PostAsync($"/api/schedule/proposals/{c.ProposalId}/approve", null);
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
            Assert.Equal("Cancelled", (await SlotAsync(factory, c.SlotIds[0])).Status);
            Assert.Equal("Approved", (await SlotAsync(factory, c.SlotIds[1])).Status);
            Assert.Single(factory.Jobs.Calls, call => call.Method == "ScheduleAllJobsForSlot");
            Assert.Equal("Scheduled", (await c.Client.GetFromJsonAsync<TaskDto>($"/api/tasks/{c.TaskId}"))!.Status);
        }

        [Fact]
        public async Task Approve_WithNoSlotLeft_Returns400_AndLeavesTheProposalPending()
        {
            using var factory = NewFactory();
            var c = await SeedAsync(factory);
            await c.Client.PostAsync($"{Url(c, 0)}/reject", null);
            await c.Client.PostAsync($"{Url(c, 1)}/reject", null);

            var response = await c.Client.PostAsync($"/api/schedule/proposals/{c.ProposalId}/approve", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Pending", (await QueryAsync(factory, db => db.ScheduleProposals.AsNoTracking().SingleAsync(p => p.Id == c.ProposalId))).Status);
        }

        // ------------------------------------------------------------ Kiểm Google Calendar khi duyệt

        [Fact]
        public async Task Approve_WithAGoogleConflict_Returns409WithDetails_AndChangesNothing()
        {
            using var factory = NewFactory();
            var c = await SeedAsync(factory);
            await ConnectGoogleAsync(factory, c.UserId);
            // Sự kiện CHỈ có trên Google (chưa được lưu): nhờ đồng bộ ngay lúc duyệt vẫn bị phát hiện. Trùng slot 0 (03:00 đến 04:00 UTC)
            factory.CalendarApi.Events["g1"] = FakeCalendarApiHandler.GoogleEvent("g1", "Họp khách", c.Day.AddHours(3).AddMinutes(30), c.Day.AddHours(4).AddMinutes(30));

            var response = await c.Client.PostAsync($"/api/schedule/proposals/{c.ProposalId}/approve", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var body = await BodyAsync(response);
            Assert.Equal("SCHEDULE_CONFLICT", body.GetProperty("code").GetString());
            var conflict = Assert.Single(body.GetProperty("details").GetProperty("conflicts").EnumerateArray());
            Assert.Equal(c.SlotIds[0], conflict.GetProperty("slotId").GetGuid());
            Assert.Equal("Viết báo cáo", conflict.GetProperty("taskTitle").GetString());
            Assert.Equal("Họp khách", conflict.GetProperty("event").GetProperty("title").GetString());

            Assert.All(await QueryAsync(factory, db => db.ScheduleSlots.AsNoTracking().Where(s => c.SlotIds.Contains(s.Id)).ToListAsync()), s => Assert.Equal("Proposed", s.Status));
            Assert.Equal("Pending", (await QueryAsync(factory, db => db.ScheduleProposals.AsNoTracking().SingleAsync(p => p.Id == c.ProposalId))).Status);
            Assert.Empty(factory.Jobs.Calls);
        }

        [Fact]
        public async Task Approve_SkipConflicts_ApprovesTheRest_CancelsTheClashingSlot_AndReportsIt()
        {
            using var factory = NewFactory();
            var c = await SeedAsync(factory);
            await ConnectGoogleAsync(factory, c.UserId);
            factory.CalendarApi.Events["g1"] = FakeCalendarApiHandler.GoogleEvent("g1", "Họp khách", c.Day.AddHours(3), c.Day.AddHours(4));

            var response = await c.Client.PostAsync($"/api/schedule/proposals/{c.ProposalId}/approve?skipConflicts=true", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = (await response.Content.ReadFromJsonAsync<ScheduleProposalDto>(Json))!;
            Assert.Equal("Approved", dto.Status);
            Assert.Equal("Checked", dto.GoogleCheck);
            Assert.Equal(c.SlotIds[0], Assert.Single(dto.SkippedConflicts!).SlotId);

            Assert.Equal("Cancelled", (await SlotAsync(factory, c.SlotIds[0])).Status);
            Assert.Equal("Approved", (await SlotAsync(factory, c.SlotIds[1])).Status);
            Assert.Equal(("Deleted", "GoogleSync"), ((await LogsAsync(factory, c.SlotIds[0])).Single().ChangeType, (await LogsAsync(factory, c.SlotIds[0])).Single().Source));
            Assert.Single(factory.Jobs.Calls, call => call.Method == "ScheduleAllJobsForSlot" && call.SlotId == c.SlotIds[1]);
        }

        [Fact]
        public async Task Approve_SkipConflicts_WhenEverySlotClashes_Returns400()
        {
            using var factory = NewFactory();
            var c = await SeedAsync(factory, slots: 1);
            await ConnectGoogleAsync(factory, c.UserId);
            factory.CalendarApi.Events["g1"] = FakeCalendarApiHandler.GoogleEvent("g1", "Cả buổi sáng", c.Day.AddHours(2), c.Day.AddHours(6));

            var response = await c.Client.PostAsync($"/api/schedule/proposals/{c.ProposalId}/approve?skipConflicts=true", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Proposed", (await SlotAsync(factory, c.SlotIds[0])).Status);
            Assert.Equal("Pending", (await QueryAsync(factory, db => db.ScheduleProposals.AsNoTracking().SingleAsync(p => p.Id == c.ProposalId))).Status);
        }

        [Fact]
        public async Task Approve_IgnoresEventsThatDoNotReallyClash()
        {
            using var factory = NewFactory();
            var c = await SeedAsync(factory, slots: 1);   // slot 03:00 đến 04:00
            await ConnectGoogleAsync(factory, c.UserId);
            var api = factory.CalendarApi.Events;
            api["touch"] = FakeCalendarApiHandler.GoogleEvent("touch", "Chạm đầu mút", c.Day.AddHours(4), c.Day.AddHours(5));          // bắt đầu đúng lúc slot kết thúc
            api["touch2"] = FakeCalendarApiHandler.GoogleEvent("touch2", "Chạm đầu mút 2", c.Day.AddHours(2), c.Day.AddHours(3));      // kết thúc đúng lúc slot bắt đầu
            api["metanoia" + Guid.NewGuid().ToString("N")] = FakeCalendarApiHandler.GoogleEvent("metanoiax", "Chính slot đã đẩy", c.Day.AddHours(3), c.Day.AddHours(4));
            api["gone"] = FakeCalendarApiHandler.GoogleEvent("gone", "Đã hủy", c.Day.AddHours(3), c.Day.AddHours(4), status: "cancelled");
            var allDay = FakeCalendarApiHandler.GoogleEvent("allday", "Sinh nhật", c.Day, c.Day.AddDays(1));
            allDay["start"] = new System.Text.Json.Nodes.JsonObject { ["date"] = c.Day.ToString("yyyy-MM-dd") };
            allDay["end"] = new System.Text.Json.Nodes.JsonObject { ["date"] = c.Day.AddDays(1).ToString("yyyy-MM-dd") };
            api["allday"] = allDay;

            var response = await c.Client.PostAsync($"/api/schedule/proposals/{c.ProposalId}/approve", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = (await response.Content.ReadFromJsonAsync<ScheduleProposalDto>(Json))!;
            Assert.Equal("Checked", dto.GoogleCheck);
            Assert.Empty(dto.SkippedConflicts!);
        }

        [Fact]
        public async Task Approve_WithoutAGoogleConnection_ApprovesNormally_AndSaysNotConnected()
        {
            using var factory = NewFactory();
            var c = await SeedAsync(factory);

            var response = await c.Client.PostAsync($"/api/schedule/proposals/{c.ProposalId}/approve", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("NotConnected", (await response.Content.ReadFromJsonAsync<ScheduleProposalDto>(Json))!.GoogleCheck);
        }

        [Fact]
        public async Task Approve_WhenTheGoogleSyncFails_FallsBackToStoredEvents_AndReportsStale()
        {
            using var factory = NewFactory();
            var c = await SeedAsync(factory);
            var connection = await ConnectGoogleAsync(factory, c.UserId);
            factory.CalendarApi.FailListWith = HttpStatusCode.InternalServerError;

            // Đồng bộ lỗi, không có xung đột trong dữ liệu đã lưu: vẫn duyệt, báo Stale
            var ok = await c.Client.PostAsync($"/api/schedule/proposals/{c.ProposalId}/approve", null);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("Stale", (await ok.Content.ReadFromJsonAsync<ScheduleProposalDto>(Json))!.GoogleCheck);

            // Có sự kiện đã lưu trùng giờ: vẫn bị phát hiện dù đồng bộ lỗi
            var d = await SeedAsync(factory, slots: 1);
            await CacheEventAsync(factory, connection, "cached", d.Day.AddHours(3), d.Day.AddHours(4));
            Assert.Equal(HttpStatusCode.Conflict, (await d.Client.PostAsync($"/api/schedule/proposals/{d.ProposalId}/approve", null)).StatusCode);
        }

        // ------------------------------------------------------------ Xem trước xung đột

        [Fact]
        public async Task Conflicts_Preview_UsesStoredEvents_AndEditReportsTheNewClash()
        {
            using var factory = NewFactory();
            var c = await SeedAsync(factory);
            var connection = await ConnectGoogleAsync(factory, c.UserId);
            await CacheEventAsync(factory, connection, "e1", c.Day.AddHours(5), c.Day.AddHours(6));      // trùng slot 1 (05:00)
            await CacheEventAsync(factory, connection, "e2", c.Day.AddHours(20), c.Day.AddHours(21));    // không trùng slot nào
            await CacheEventAsync(factory, connection, "e3", c.Day.AddHours(3), c.Day.AddHours(4), cancelled: true);

            var preview = (await c.Client.GetFromJsonAsync<ProposalConflictsDto>($"/api/schedule/proposals/{c.ProposalId}/conflicts", Json))!;
            Assert.Equal("Cached", preview.GoogleCheck);
            Assert.Equal(c.SlotIds[1], Assert.Single(preview.Conflicts).SlotId);

            // Kéo slot 0 sang giờ của sự kiện e2: câu trả lời của việc sửa cho biết ngay
            var moved = (await (await c.Client.PutAsJsonAsync(Url(c, 0), Times(c.Day.AddHours(20)))).Content.ReadFromJsonAsync<ProposedSlotUpdateResultDto>(Json))!;
            Assert.Equal("Họp khách", Assert.Single(moved.GoogleConflicts).Event.Title);
        }

        [Fact]
        public async Task Conflicts_Preview_NotConnected_And_OthersProposal_And_Login()
        {
            using var factory = NewFactory();
            var c = await SeedAsync(factory);

            var none = (await c.Client.GetFromJsonAsync<ProposalConflictsDto>($"/api/schedule/proposals/{c.ProposalId}/conflicts", Json))!;
            Assert.Equal("NotConnected", none.GoogleCheck);
            Assert.Empty(none.Conflicts);

            var (stranger, _) = await TestHelpers.LoginAsync(factory, "stranger");
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/schedule/proposals/{c.ProposalId}/conflicts")).StatusCode);

            var anonymous = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/schedule/proposals/{c.ProposalId}/conflicts")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"{Url(c)}/reject", null)).StatusCode);
        }
    }
}
