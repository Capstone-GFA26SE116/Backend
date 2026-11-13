using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using METANOIA.Application.Dto;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using METANOIA.Infrastructure.Services;
using METANOIA.Test.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DomainEntities = METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // POST /api/google-calendar/sync?from=&to=&direction=: chọn hướng và khoảng ngày; chiều METANOIA -> Google đẩy slot lên Google
    public class GoogleSyncRangeTests
    {
        private const string Sync = "/api/google-calendar/sync";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        private sealed record Ctx(HttpClient Client, Guid UserId, Guid TaskId, DateOnly D0, string TaskTitle);

        private static ApiFactory NewFactory() => new() { FakeCalendarApi = true };

        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        // Người dùng múi giờ Việt Nam (UTC+7), một task, đã kết nối Google. D0 là một ngày ở tương lai gần
        private static async Task<Ctx> SetupAsync(ApiFactory factory, string? syncToken = null)
        {
            var domainId = Guid.NewGuid();
            await factory.SeedAsync(db => db.Domains.Add(new DomainEntities.Domain { Id = domainId, Name = $"Dev-{Guid.NewGuid()}", IsActive = true }));
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");

            var settings = await client.PutAsJsonAsync("/api/me/settings", new UpdateSettingsRequestDto
            {
                WorkDayStart = new TimeOnly(8, 0), WorkDayEnd = new TimeOnly(18, 0), WorksOnWeekend = false,
                DefaultBufferMinutes = 10, ReminderLeadMinutes = 15, TimeZone = "Asia/Ho_Chi_Minh"
            });
            Assert.Equal(HttpStatusCode.OK, settings.StatusCode);

            var project = (await (await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Dự án A" })).Content.ReadFromJsonAsync<ProjectDto>())!;
            var task = (await (await client.PostAsJsonAsync($"/api/projects/{project.Id}/tasks", new TaskRequestDto
            {
                Title = "Viết báo cáo", TaskType = "Flexible", DomainId = domainId,
                Deadline = new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero), EstimatedMinutes = 60
            })).Content.ReadFromJsonAsync<TaskDto>())!;

            await factory.SeedAsync(db => db.GoogleConnections.Add(new GoogleConnection
            {
                Id = Guid.NewGuid(), UserId = auth.UserId, Status = "Active", SyncToken = syncToken,
                AccessTokenEncrypted = new byte[] { 1 }, RefreshTokenEncrypted = new byte[] { 1 },
                TokenExpiresAt = DateTime.UtcNow.AddHours(1), CreatedAt = DateTime.UtcNow
            }));

            var d0 = DateOnly.FromDateTime(factory.Clock.GetUtcNow().UtcDateTime).AddDays(10);
            return new Ctx(client, auth.UserId, task.Id, d0, "Viết báo cáo");
        }

        // Slot bắt đầu 03:00 UTC (10:00 giờ Việt Nam) của ngày đã cho
        private static async Task<ScheduleSlotDto> SlotAsync(Ctx c, DateOnly day, string status = "Approved")
        {
            var start = new DateTimeOffset(day.ToDateTime(new TimeOnly(3, 0)), TimeSpan.Zero);
            var response = await c.Client.PostAsJsonAsync($"/api/tasks/{c.TaskId}/schedule-slots", new ScheduleSlotRequestDto
            { StartTime = start, EndTime = start.AddMinutes(60), IsManualOverride = true, Status = status });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ScheduleSlotDto>())!;
        }

        private static async Task<GoogleCalendarSyncResultDto> RunAsync(Ctx c, string query)
        {
            var response = await c.Client.PostAsync($"{Sync}{query}", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<GoogleCalendarSyncResultDto>(Json))!;
        }

        private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

        private static DateTime ParseUtc(string? value) => DateTimeOffset.Parse(value!).UtcDateTime;

        private static Task<ScheduleSlot> SlotRowAsync(ApiFactory f, Guid id) => QueryAsync(f, db => db.ScheduleSlots.AsNoTracking().SingleAsync(s => s.Id == id));

        // ------------------------------------------------------------ Kéo từ Google về: chọn ngày

        [Fact]
        public async Task WithoutRangeOrDirection_KeepsTheOldBehavior_IncrementalSyncByToken()
        {
            using var factory = NewFactory();
            var c = await SetupAsync(factory, syncToken: "old-token");
            factory.CalendarApi.Events["g1"] = FakeCalendarApiHandler.GoogleEvent("g1", "Họp", DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1));
            factory.CalendarApi.NextSyncToken = "new-token";

            var result = await RunAsync(c, "");

            var list = Assert.Single(factory.CalendarApi.Calls);
            Assert.Equal("old-token", list.Param("syncToken"));
            Assert.Null(list.Param("timeMin"));
            Assert.Null(list.Param("timeMax"));
            Assert.Equal("Incremental", result.Mode);
            Assert.Equal(GoogleSyncDirection.FromGoogle, result.Direction);
            Assert.Equal(1, result.Added);
            Assert.Equal("new-token", (await QueryAsync(factory, db => db.GoogleConnections.SingleAsync())).SyncToken);
            Assert.Empty(factory.CalendarApi.Of("POST"));   // mặc định không đẩy gì lên Google
        }

        [Fact]
        public async Task FromOnly_SyncsFromThatDayOnwards_InTheUsersTimeZone_AndLeavesTheSyncTokenAlone()
        {
            using var factory = NewFactory();
            var c = await SetupAsync(factory, syncToken: "keep-me");
            factory.CalendarApi.NextSyncToken = "must-not-be-saved";

            var result = await RunAsync(c, $"?from={D(c.D0)}");

            var list = Assert.Single(factory.CalendarApi.Calls);
            // Đầu ngày D0 theo giờ Việt Nam = 17:00 UTC của ngày hôm trước
            Assert.Equal(c.D0.AddDays(-1).ToDateTime(new TimeOnly(17, 0)), ParseUtc(list.Param("timeMin")));
            Assert.Null(list.Param("timeMax"));                    // không chọn to: lấy hết về sau
            Assert.Null(list.Param("syncToken"));                  // Google không cho dùng syncToken cùng timeMin
            Assert.Equal("Range", result.Mode);
            Assert.NotNull(result.RangeFromUtc);
            Assert.Null(result.RangeToUtc);
            Assert.Equal("keep-me", (await QueryAsync(factory, db => db.GoogleConnections.SingleAsync())).SyncToken);
        }

        [Fact]
        public async Task FromAndTo_IncludeTheWholeLastDay()
        {
            using var factory = NewFactory();
            var c = await SetupAsync(factory);

            await RunAsync(c, $"?from={D(c.D0)}&to={D(c.D0.AddDays(2))}");

            var list = Assert.Single(factory.CalendarApi.Calls);
            Assert.Equal(c.D0.AddDays(-1).ToDateTime(new TimeOnly(17, 0)), ParseUtc(list.Param("timeMin")));
            // Hết ngày to (giờ Việt Nam) = đầu ngày kế tiếp = 17:00 UTC của chính ngày to
            Assert.Equal(c.D0.AddDays(2).ToDateTime(new TimeOnly(17, 0)), ParseUtc(list.Param("timeMax")));
        }

        [Fact]
        public async Task ToOnly_SyncsEverythingUpToThatDay()
        {
            using var factory = NewFactory();
            var c = await SetupAsync(factory);

            await RunAsync(c, $"?to={D(c.D0)}");

            var list = Assert.Single(factory.CalendarApi.Calls);
            Assert.Null(list.Param("timeMin"));
            Assert.Equal(c.D0.ToDateTime(new TimeOnly(17, 0)), ParseUtc(list.Param("timeMax")));
        }

        [Fact]
        public async Task ToBeforeFrom_Returns400_AndDoesNotCallGoogle()
        {
            using var factory = NewFactory();
            var c = await SetupAsync(factory);

            var response = await c.Client.PostAsync($"{Sync}?from={D(c.D0)}&to={D(c.D0.AddDays(-1))}", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("to", await response.Content.ReadAsStringAsync());
            Assert.Empty(factory.CalendarApi.Calls);
        }

        [Fact]
        public async Task EventPushedByMetanoia_IsRecognisedWhenPulledBack_SoItIsNotShownTwice()
        {
            using var factory = NewFactory();
            var c = await SetupAsync(factory);
            var id = "metanoia" + Guid.NewGuid().ToString("N");
            factory.CalendarApi.Events[id] = FakeCalendarApiHandler.GoogleEvent(id, "Viết báo cáo", DateTime.UtcNow.AddDays(3), DateTime.UtcNow.AddDays(3).AddHours(1));
            factory.CalendarApi.Events["other"] = FakeCalendarApiHandler.GoogleEvent("other", "Họp khách", DateTime.UtcNow.AddDays(3), DateTime.UtcNow.AddDays(3).AddHours(1));

            await RunAsync(c, $"?from={D(c.D0)}");

            var rows = await QueryAsync(factory, db => db.GoogleCalendarEvents.AsNoTracking().ToListAsync());
            Assert.True(rows.Single(r => r.GoogleEventId == id).IsCreatedByMetanoia);
            Assert.False(rows.Single(r => r.GoogleEventId == "other").IsCreatedByMetanoia);
        }

        // ------------------------------------------------------------ Đẩy slot lên Google

        [Fact]
        public async Task ToGoogle_PushesOnlyApprovedAndLiveSlotsInTheRange()
        {
            using var factory = NewFactory();
            var c = await SetupAsync(factory);
            var inRange = await SlotAsync(c, c.D0.AddDays(1));
            var outOfRange = await SlotAsync(c, c.D0.AddDays(8));
            var proposed = await SlotAsync(c, c.D0.AddDays(1), "Proposed");
            var done = await SlotAsync(c, c.D0.AddDays(2));
            Assert.Equal(HttpStatusCode.OK, (await c.Client.PutAsJsonAsync($"/api/schedule-slots/{done.Id}", new ScheduleSlotRequestDto
            { StartTime = done.StartTime, EndTime = done.EndTime, IsManualOverride = true, Status = "Done" })).StatusCode);

            var result = await RunAsync(c, $"?from={D(c.D0)}&to={D(c.D0.AddDays(5))}&direction=ToGoogle");

            Assert.Equal(1, result.PushedCreated);
            Assert.Equal(0, result.PushFailed);
            Assert.Empty(factory.CalendarApi.Of("GET"));   // chỉ đẩy, không kéo về

            var posted = Assert.Single(factory.CalendarApi.Of("POST")).Json!;
            Assert.Equal("metanoia" + inRange.Id.ToString("N"), posted["id"]!.GetValue<string>());
            Assert.Equal(c.TaskTitle, posted["summary"]!.GetValue<string>());
            Assert.Equal(inRange.StartTime.UtcDateTime, ParseUtc(posted["start"]!["dateTime"]!.GetValue<string>()));
            Assert.Equal(inRange.EndTime.UtcDateTime, ParseUtc(posted["end"]!["dateTime"]!.GetValue<string>()));
            Assert.Equal(inRange.Id.ToString(), posted["extendedProperties"]!["private"]!["metanoiaSlotId"]!.GetValue<string>());

            Assert.Equal("metanoia" + inRange.Id.ToString("N"), (await SlotRowAsync(factory, inRange.Id)).GoogleEventId);
            Assert.Null((await SlotRowAsync(factory, outOfRange.Id)).GoogleEventId);
            Assert.Null((await SlotRowAsync(factory, proposed.Id)).GoogleEventId);
            Assert.Null((await SlotRowAsync(factory, done.Id)).GoogleEventId);

            // Có dòng cache đánh dấu "do METANOIA tạo" để lịch gộp không hiện trùng với slot
            var local = await QueryAsync(factory, db => db.GoogleCalendarEvents.AsNoTracking().SingleAsync());
            Assert.True(local.IsCreatedByMetanoia);
        }

        [Fact]
        public async Task ToGoogle_WithoutDates_PushesEveryLiveSlot_AndTheTitleIncludesTheSubtask()
        {
            using var factory = NewFactory();
            var c = await SetupAsync(factory);
            var sub = (await (await c.Client.PostAsJsonAsync($"/api/tasks/{c.TaskId}/subtasks",
                new SubTaskRequestDto { Title = "Phần 1", EstimatedMinutes = 20, OrderIndex = 1 })).Content.ReadFromJsonAsync<SubTaskDto>())!;
            var start = new DateTimeOffset(c.D0.AddDays(30).ToDateTime(new TimeOnly(3, 0)), TimeSpan.Zero);
            var slot = (await (await c.Client.PostAsJsonAsync($"/api/tasks/{c.TaskId}/schedule-slots", new ScheduleSlotRequestDto
            { SubTaskId = sub.Id, StartTime = start, EndTime = start.AddMinutes(30), IsManualOverride = true, Status = "Approved" })).Content.ReadFromJsonAsync<ScheduleSlotDto>())!;
            await SlotAsync(c, c.D0.AddDays(1));

            var result = await RunAsync(c, "?direction=ToGoogle");

            Assert.Equal(2, result.PushedCreated);
            Assert.Equal("Incremental", result.Mode);
            Assert.Contains(factory.CalendarApi.Of("POST"), p => p.Json!["summary"]!.GetValue<string>() == "Viết báo cáo - Phần 1");
            Assert.NotNull((await SlotRowAsync(factory, slot.Id)).GoogleEventId);
        }

        [Fact]
        public async Task PushingTwice_UpdatesTheSameEvent_InsteadOfCreatingADuplicate()
        {
            using var factory = NewFactory();
            var c = await SetupAsync(factory);
            var slot = await SlotAsync(c, c.D0.AddDays(1));
            var query = $"?from={D(c.D0)}&direction=ToGoogle";

            await RunAsync(c, query);
            var second = await RunAsync(c, query);

            Assert.Equal(0, second.PushedCreated);
            Assert.Equal(1, second.PushedUpdated);
            Assert.Single(factory.CalendarApi.Of("POST"));
            Assert.Single(factory.CalendarApi.Of("PUT"));
            Assert.Single(factory.CalendarApi.Events);
        }

        [Fact]
        public async Task EventDeletedOnGoogle_IsRecreatedOnTheNextPush()
        {
            using var factory = NewFactory();
            var c = await SetupAsync(factory);
            var slot = await SlotAsync(c, c.D0.AddDays(1));
            var query = $"?from={D(c.D0)}&direction=ToGoogle";
            await RunAsync(c, query);
            factory.CalendarApi.Events.Clear();   // người dùng xóa sự kiện bên Google

            var result = await RunAsync(c, query);

            Assert.Equal(1, result.PushedCreated);
            Assert.Single(factory.CalendarApi.Events);
            Assert.Equal("metanoia" + slot.Id.ToString("N"), (await SlotRowAsync(factory, slot.Id)).GoogleEventId);
        }

        [Fact]
        public async Task SlotCancelledAfterPush_DeletesTheGoogleEvent_ButDoneSlotsKeepTheirs()
        {
            using var factory = NewFactory();
            var c = await SetupAsync(factory);
            var cancelled = await SlotAsync(c, c.D0.AddDays(1));
            var done = await SlotAsync(c, c.D0.AddDays(2));
            var query = $"?from={D(c.D0)}&direction=ToGoogle";
            await RunAsync(c, query);
            Assert.Equal(2, factory.CalendarApi.Events.Count);

            Assert.Equal(HttpStatusCode.OK, (await c.Client.DeleteAsync($"/api/schedule-slots/{cancelled.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await c.Client.PutAsJsonAsync($"/api/schedule-slots/{done.Id}", new ScheduleSlotRequestDto
            { StartTime = done.StartTime, EndTime = done.EndTime, IsManualOverride = true, Status = "Done" })).StatusCode);

            var result = await RunAsync(c, query);

            Assert.Equal(1, result.PushedDeleted);
            Assert.Single(factory.CalendarApi.Of("DELETE"));
            Assert.DoesNotContain("metanoia" + cancelled.Id.ToString("N"), factory.CalendarApi.Events.Keys);
            Assert.Contains("metanoia" + done.Id.ToString("N"), factory.CalendarApi.Events.Keys);
            Assert.Null((await SlotRowAsync(factory, cancelled.Id)).GoogleEventId);
            Assert.NotNull((await SlotRowAsync(factory, done.Id)).GoogleEventId);
            var row = await QueryAsync(factory, db => db.GoogleCalendarEvents.AsNoTracking().SingleAsync(e => e.GoogleEventId == "metanoia" + cancelled.Id.ToString("N")));
            Assert.True(row.IsCancelled);
        }

        [Fact]
        public async Task Both_PullsFirst_ThenPushes_AndReportsBothSides()
        {
            using var factory = NewFactory();
            var c = await SetupAsync(factory);
            factory.CalendarApi.Events["g1"] = FakeCalendarApiHandler.GoogleEvent("g1", "Họp", DateTime.UtcNow.AddDays(3), DateTime.UtcNow.AddDays(3).AddHours(1));
            await SlotAsync(c, c.D0.AddDays(1));

            var result = await RunAsync(c, $"?from={D(c.D0)}&direction=Both");

            Assert.Equal(GoogleSyncDirection.Both, result.Direction);
            Assert.Equal(1, result.Added);
            Assert.Equal(1, result.PushedCreated);
            var methods = factory.CalendarApi.Calls.Select(x => x.Method).ToList();
            Assert.True(methods.IndexOf("GET") < methods.IndexOf("POST"));   // kéo về trước, đẩy lên sau
        }

        [Fact]
        public async Task PushFailure_MarksOnlyThatSlot_AndTheRestOfTheSyncStillSucceeds()
        {
            using var factory = NewFactory();
            var c = await SetupAsync(factory);
            var slot = await SlotAsync(c, c.D0.AddDays(1));
            factory.CalendarApi.FailInsertWith = HttpStatusCode.InternalServerError;

            var result = await RunAsync(c, $"?from={D(c.D0)}&direction=ToGoogle");

            Assert.Equal(1, result.PushFailed);
            Assert.Equal(0, result.PushedCreated);
            Assert.Contains(slot.Id.ToString(), Assert.Single(result.PushErrors));
            var row = await SlotRowAsync(factory, slot.Id);
            Assert.True(row.SyncFailed);
            Assert.Null(row.GoogleEventId);

            // Lần sau Google ổn: đẩy được và cờ lỗi được xóa
            factory.CalendarApi.FailInsertWith = null;
            var retry = await RunAsync(c, $"?from={D(c.D0)}&direction=ToGoogle");
            Assert.Equal(1, retry.PushedCreated);
            Assert.False((await SlotRowAsync(factory, slot.Id)).SyncFailed);
        }

        [Fact]
        public async Task OtherUsersSlots_AreNeverPushed()
        {
            using var factory = NewFactory();
            var c = await SetupAsync(factory);
            await SlotAsync(c, c.D0.AddDays(1));

            var (other, otherAuth) = await TestHelpers.LoginAsync(factory, "user-2");
            await factory.SeedAsync(db => db.GoogleConnections.Add(new GoogleConnection
            {
                Id = Guid.NewGuid(), UserId = otherAuth.UserId, Status = "Active",
                AccessTokenEncrypted = new byte[] { 1 }, RefreshTokenEncrypted = new byte[] { 1 },
                TokenExpiresAt = DateTime.UtcNow.AddHours(1), CreatedAt = DateTime.UtcNow
            }));

            var response = await other.PostAsync($"{Sync}?direction=ToGoogle", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(factory.CalendarApi.Of("POST"));   // user-2 không có slot nào, không đụng slot của user-1
        }
    }
}
