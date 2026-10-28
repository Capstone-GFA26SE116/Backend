using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using METANOIA.Application.Dto;
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
    // Bấm giờ làm việc (start, stop, current, confirm, self-report, time-summary), các job nền theo slot và hộp thư thông báo.
    // Đồng hồ server là factory.Clock (chỉnh tay) và Hangfire được thay bằng factory.Jobs (ghi lại việc hẹn và hủy job).
    public class FocusSessionTests
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private sealed record Setup(HttpClient Client, Guid TaskId, Guid UserId);

        private static async Task<Setup> SetupAsync(ApiFactory factory, string subject = "user-1")
        {
            var domainId = Guid.NewGuid();
            await factory.SeedAsync(db => db.Domains.Add(new DomainEntities.Domain
            {
                Id = domainId,
                Name = $"Dev-{Guid.NewGuid()}",
                IsActive = true
            }));

            var (client, auth) = await TestHelpers.LoginAsync(factory, subject);
            var project = await (await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Website" }))
                .Content.ReadFromJsonAsync<ProjectDto>();
            var task = await (await client.PostAsJsonAsync($"/api/projects/{project!.Id}/tasks", new TaskRequestDto
            {
                Title = "Viết báo cáo",
                TaskType = "Flexible",
                DomainId = domainId,
                Deadline = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                EstimatedMinutes = 120
            })).Content.ReadFromJsonAsync<TaskDto>();

            return new Setup(client, task!.Id, auth.UserId);
        }

        private static async Task<ScheduleSlotDto> CreateSlotAsync(
            ApiFactory factory, HttpClient client, Guid taskId, int startInMinutes, int lengthMinutes, string status = "Approved")
        {
            var start = factory.Clock.GetUtcNow().AddMinutes(startInMinutes);
            var response = await client.PostAsJsonAsync($"/api/tasks/{taskId}/schedule-slots", new ScheduleSlotRequestDto
            {
                StartTime = start,
                EndTime = start.AddMinutes(lengthMinutes),
                IsManualOverride = true,
                Status = status
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ScheduleSlotDto>())!;
        }

        private static Task<HttpResponseMessage> StartAsync(HttpClient client, Guid taskId, Guid? slotId = null, string route = "/api/focus/start")
            => client.PostAsJsonAsync(route, new { taskId, scheduleSlotId = slotId });

        private static Task<HttpResponseMessage> StopAsync(HttpClient client, Guid sessionId, string reason, string? note = null)
            => client.PostAsJsonAsync($"/api/focus/{sessionId}/stop", new { stopReason = reason, note });

        private static async Task<FocusStartResponse> StartOkAsync(HttpClient client, Guid taskId, Guid? slotId = null)
        {
            var response = await StartAsync(client, taskId, slotId);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<FocusStartResponse>(Json))!;
        }

        private static async Task<FocusStopResponse> StopOkAsync(HttpClient client, Guid sessionId, string reason)
        {
            var response = await StopAsync(client, sessionId, reason);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<FocusStopResponse>(Json))!;
        }

        private static async Task<FocusTimeSummaryResponse> SummaryAsync(HttpClient client, Guid slotId)
        {
            var response = await client.GetAsync($"/api/focus/{slotId}/time-summary");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<FocusTimeSummaryResponse>(Json))!;
        }

        private static async Task<string> SlotStatusAsync(HttpClient client, Guid slotId)
        {
            var slot = await client.GetFromJsonAsync<ScheduleSlotDto>($"/api/schedule-slots/{slotId}");
            return slot!.Status;
        }

        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static async Task RunJobAsync(ApiFactory factory, Func<SlotBackgroundJobs, Task> job)
        {
            using var scope = factory.Services.CreateScope();
            await job(scope.ServiceProvider.GetRequiredService<SlotBackgroundJobs>());
        }

        // Bảng Notification có CHECK trên Type chỉ cho các giá trị này (Notification_Type_check)
        private static readonly string[] AllowedStoredTypes = { "SlotReminder", "MissedSlot", "DeadlineRisk" };

        // Loại thông báo chi tiết (gửi cho SignalR và Web Push) và tiêu đề tương ứng. Trong DB chỉ lưu nhóm lớn nên lọc theo tiêu đề
        private static readonly Dictionary<string, string> TitleOf = new()
        {
            ["PRE_START_REMINDER"] = "Sắp đến giờ làm việc",
            ["START_PROMPT"] = "Đến giờ làm việc",
            ["NOT_STARTED"] = "Bạn chưa bắt đầu",
            ["NEAR_END"] = "Sắp hết giờ",
            ["OVERRUN"] = "Đã quá giờ dự kiến",
            ["AUTO_TIMEOUT"] = "Phiên làm việc bị ngắt",
            ["CHECK_IN"] = "Công việc đã hết giờ",
            ["MISSED_SLOT"] = "Bạn đã bỏ lỡ công việc"
        };

        private static Task<List<DomainEntities.Notification>> NotificationsAsync(ApiFactory factory, string? action = null) =>
            QueryAsync(factory, db => db.Notifications
                .Where(n => action == null || n.Title == TitleOf[action]).ToListAsync());

        // ---------------------------------------------------------------- Start

        [Fact]
        public async Task Start_WithoutSlot_CreatesARunningTimerSession()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);

            var started = await StartOkAsync(s.Client, s.TaskId);

            Assert.Equal(METANOIA.Domain.Enums.CaptureMethod.Timer, started.CaptureMethod);
            Assert.Equal(factory.Clock.GetUtcNow(), started.StartedAt);
            Assert.Equal(0, started.PlannedMinutes);
            Assert.Equal(0, started.StartedEarlyMinutes);
            Assert.Null(started.Warning);

            var session = await QueryAsync(factory, db => db.FocusSessions.SingleAsync());
            Assert.Null(session.EndedAt);
            Assert.True(session.IsConfirmed);
            Assert.Equal("Timer", session.CaptureMethod);
        }

        [Fact]
        public async Task Start_Twice_Returns409_WithTheRunningSession_AndCreatesNothingNew()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var first = await StartOkAsync(s.Client, s.TaskId);

            // Tab thứ hai (cũng thử bằng đường dẫn cũ /api/FocusSession) nhận lại phiên đang chạy
            var second = await StartAsync(s.Client, s.TaskId, route: "/api/FocusSession/start");

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
            Assert.Equal(first.SessionId, body.RootElement.GetProperty("details").GetProperty("sessionId").GetGuid());
            Assert.Equal("Viết báo cáo", body.RootElement.GetProperty("details").GetProperty("taskTitle").GetString());
            Assert.Equal(1, await QueryAsync(factory, db => db.FocusSessions.CountAsync()));
        }

        [Fact]
        public async Task Start_OnSomeoneElsesTask_Returns404()
        {
            using var factory = new ApiFactory();
            var owner = await SetupAsync(factory, "owner");
            var (other, _) = await TestHelpers.LoginAsync(factory, "other");

            var response = await StartAsync(other, owner.TaskId);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(0, await QueryAsync(factory, db => db.FocusSessions.CountAsync()));
        }

        [Fact]
        public async Task Start_WithSlotOfAnotherTask_Returns400()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, 30);
            var project = (await s.Client.GetFromJsonAsync<List<ProjectDto>>("/api/projects"))!.First();
            var otherTask = await (await s.Client.PostAsJsonAsync($"/api/projects/{project.Id}/tasks", new TaskRequestDto
            {
                Title = "Việc khác",
                TaskType = "Fixed",
                FixedStart = factory.Clock.GetUtcNow(),
                FixedEnd = factory.Clock.GetUtcNow().AddHours(1),
                DomainId = (await QueryAsync(factory, db => db.Domains.FirstAsync())).Id
            })).Content.ReadFromJsonAsync<TaskDto>();

            var response = await StartAsync(s.Client, otherTask!.Id, slot.Id);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Start_WithAProposedSlot_Returns400_UntilItIsApproved()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, 30, status: "Proposed");

            var response = await StartAsync(s.Client, s.TaskId, slot.Id);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Proposed", await SlotStatusAsync(s.Client, slot.Id));
        }

        [Fact]
        public async Task Start_WithUnknownSlotOrTrack_IsRejected()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);

            Assert.Equal(HttpStatusCode.NotFound, (await StartAsync(s.Client, s.TaskId, Guid.NewGuid())).StatusCode);
            var withTrack = await s.Client.PostAsJsonAsync("/api/focus/start", new { taskId = s.TaskId, focusTrackId = Guid.NewGuid() });
            Assert.Equal(HttpStatusCode.BadRequest, withTrack.StatusCode);
            Assert.Equal(0, await QueryAsync(factory, db => db.FocusSessions.CountAsync()));
        }

        [Fact]
        public async Task Start_WithSlot_SetsInProgress_ReportsEarlyStart_AndReschedulesJobsFromTheRealStart()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, startInMinutes: 10, lengthMinutes: 60);

            var started = await StartOkAsync(s.Client, s.TaskId, slot.Id);

            Assert.Equal(60, started.PlannedMinutes);
            Assert.Equal(10, started.StartedEarlyMinutes);
            Assert.Equal(started.StartedAt.AddMinutes(60), started.ExpectedEndAt);
            Assert.Null(started.Warning);
            Assert.Equal("InProgress", await SlotStatusAsync(s.Client, slot.Id));

            // Job cuối được hẹn lại theo giờ bắt đầu thực tế, còn giờ trên lịch của slot thì không bị dời
            var reschedule = Assert.Single(factory.Jobs.CallsFor("RescheduleSlotJobs", slot.Id));
            Assert.Equal(factory.Clock.GetUtcNow(), reschedule.At);
            var after = (await s.Client.GetFromJsonAsync<ScheduleSlotDto>($"/api/schedule-slots/{slot.Id}"))!;
            Assert.Equal(slot.StartTime, after.StartTime);
            Assert.Equal(slot.EndTime, after.EndTime);
        }

        [Fact]
        public async Task Start_MoreThanTwoHoursEarly_IsAllowed_ButWarns()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, startInMinutes: 180, lengthMinutes: 30);

            var started = await StartOkAsync(s.Client, s.TaskId, slot.Id);

            Assert.Equal(180, started.StartedEarlyMinutes);
            Assert.Equal("STARTED_VERY_EARLY", started.Warning);
        }

        // ---------------------------------------------------------------- Stop

        [Fact]
        public async Task Stop_Twice_Returns409_WithTheOriginalEndTime()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var started = await StartOkAsync(s.Client, s.TaskId);
            factory.Clock.Advance(TimeSpan.FromMinutes(10));
            var first = await StopOkAsync(s.Client, started.SessionId, "Completed");
            factory.Clock.Advance(TimeSpan.FromMinutes(5));

            var second = await StopAsync(s.Client, started.SessionId, "Completed");

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
            Assert.Equal(first.EndedAt, body.RootElement.GetProperty("details").GetProperty("endedAt").GetDateTimeOffset());
        }

        [Fact]
        public async Task Stop_ByAnotherUser_Returns403_AndUnknownSession_Returns404()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory, "owner");
            var (other, _) = await TestHelpers.LoginAsync(factory, "other");
            var started = await StartOkAsync(s.Client, s.TaskId);

            Assert.Equal(HttpStatusCode.Forbidden, (await StopAsync(other, started.SessionId, "Completed")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await StopAsync(s.Client, Guid.NewGuid(), "Completed")).StatusCode);
            Assert.Null((await QueryAsync(factory, db => db.FocusSessions.SingleAsync())).EndedAt);
        }

        [Fact]
        public async Task Stop_AcrossSeveralSessions_SumsTheTaskTotal_AndMovesTheSlotThroughItsStates()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, startInMinutes: 0, lengthMinutes: 60);

            // Phiên 1: làm 25 phút rồi bị gián đoạn. "Tạm dừng" = Stop (Interrupted), không có API pause
            var first = await StartOkAsync(s.Client, s.TaskId, slot.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(25));
            var stop1 = await StopOkAsync(s.Client, first.SessionId, "Interrupted");

            Assert.Equal(25, stop1.SessionMinutes);
            Assert.Equal(25, stop1.TaskTotalMinutes);
            Assert.False(stop1.CheckInRequired);
            Assert.Equal("Due", await SlotStatusAsync(s.Client, slot.Id));
            Assert.Single(factory.Jobs.CallsFor("CancelOverrunJobs", slot.Id));
            Assert.Empty(factory.Jobs.CallsFor("CancelAllJobs", slot.Id));

            // Phiên 2: nghỉ 5 phút, làm tiếp 40 phút 30 giây rồi xong
            factory.Clock.Advance(TimeSpan.FromMinutes(5));
            var second = await StartOkAsync(s.Client, s.TaskId, slot.Id);
            factory.Clock.Advance(TimeSpan.FromSeconds(40 * 60 + 30));
            var stop2 = await StopOkAsync(s.Client, second.SessionId, "Completed");

            Assert.Equal(40.5, stop2.SessionMinutes);
            Assert.Equal(65.5, stop2.TaskTotalMinutes);
            Assert.True(stop2.CheckInRequired);
            Assert.Equal("Done", await SlotStatusAsync(s.Client, slot.Id));
            Assert.Single(factory.Jobs.CallsFor("CancelAllJobs", slot.Id));

            // T_act của slot = tổng các phiên
            var summary = await SummaryAsync(s.Client, slot.Id);
            Assert.Equal(60, summary.PlannedMinutes);
            Assert.Equal(65.5, summary.ActualMinutes);
            Assert.Equal(2, summary.SessionCount);
            Assert.Equal(METANOIA.Domain.Enums.FocusStopReason.Interrupted, summary.Sessions[0].StopReason);
            Assert.Equal(METANOIA.Domain.Enums.FocusStopReason.Completed, summary.Sessions[1].StopReason);
            Assert.All(summary.Sessions, x => Assert.Equal(METANOIA.Domain.Enums.CaptureMethod.Timer, x.CaptureMethod));
        }

        [Fact]
        public async Task Stop_BeforeThePlannedEnd_IsAccepted_AndCancelsTheOverrunJobs()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, 60);
            var started = await StartOkAsync(s.Client, s.TaskId, slot.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(40));

            var stop = await StopOkAsync(s.Client, started.SessionId, "Completed");

            Assert.Equal(40, stop.SessionMinutes);
            Assert.Equal("Done", await SlotStatusAsync(s.Client, slot.Id));
            Assert.NotEmpty(factory.Jobs.CallsFor("CancelAllJobs", slot.Id));
        }

        // ---------------------------------------------------------------- Current

        [Fact]
        public async Task Current_WithoutASession_Returns204()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);

            Assert.Equal(HttpStatusCode.NoContent, (await s.Client.GetAsync("/api/focus/current")).StatusCode);
        }

        [Fact]
        public async Task Current_ReportsElapsedTimeFromTheServerClock_AndOverrun()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, lengthMinutes: 1);
            var started = await StartOkAsync(s.Client, s.TaskId, slot.Id);

            factory.Clock.Advance(TimeSpan.FromSeconds(30));
            var early = (await s.Client.GetFromJsonAsync<FocusCurrentResponse>("/api/focus/current", Json))!;
            Assert.Equal(started.SessionId, early.SessionId);
            Assert.Equal("Viết báo cáo", early.TaskTitle);
            Assert.Equal(slot.Id, early.ScheduleSlotId);
            Assert.Equal(30, early.ElapsedSeconds);
            Assert.Equal(factory.Clock.GetUtcNow(), early.ServerTime);
            Assert.False(early.IsOverrun);

            factory.Clock.Advance(TimeSpan.FromSeconds(60));
            var late = (await s.Client.GetFromJsonAsync<FocusCurrentResponse>("/api/focus/current", Json))!;
            Assert.Equal(90, late.ElapsedSeconds);
            Assert.True(late.IsOverrun);
        }

        // ---------------------------------------------------------------- Auto-timeout and confirm

        [Fact]
        public async Task AutoTimeout_ForcesTheStop_IsExcludedFromLearning_UntilTheUserConfirms()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, lengthMinutes: 30);
            var started = await StartOkAsync(s.Client, s.TaskId, slot.Id);

            // Quá 2 lần thời gian dự kiến mà không ai bấm Stop
            factory.Clock.Advance(TimeSpan.FromMinutes(61));
            await RunJobAsync(factory, jobs => jobs.AutoTimeout(slot.Id));

            var session = await QueryAsync(factory, db => db.FocusSessions.SingleAsync());
            Assert.NotNull(session.EndedAt);
            Assert.Equal("AutoTimeout", session.CaptureMethod);
            Assert.False(session.IsConfirmed);
            Assert.Equal(HttpStatusCode.NoContent, (await s.Client.GetAsync("/api/focus/current")).StatusCode);
            Assert.Equal("Done", await SlotStatusAsync(s.Client, slot.Id));
            Assert.Single(await NotificationsAsync(factory, "AUTO_TIMEOUT"));

            // Chưa xác nhận: không được tính vào thời gian thực tế
            var before = await SummaryAsync(s.Client, slot.Id);
            Assert.Equal(0, before.SessionCount);
            Assert.Equal(0, before.ActualMinutes);
            Assert.Equal(HttpStatusCode.Conflict, (await StopAsync(s.Client, started.SessionId, "Completed")).StatusCode);

            // Người dùng nói thật ra chỉ làm 45 phút: thành SelfReported đã xác nhận
            var confirm = await s.Client.PostAsJsonAsync($"/api/focus/{started.SessionId}/confirm",
                new { confirmed = true, correctedMinutes = 45 });
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

            var after = await SummaryAsync(s.Client, slot.Id);
            Assert.Equal(1, after.SessionCount);
            Assert.Equal(45, after.ActualMinutes);
            Assert.Equal(METANOIA.Domain.Enums.CaptureMethod.SelfReported, after.Sessions[0].CaptureMethod);

            // Đã xác nhận rồi thì không xác nhận lại được
            var again = await s.Client.PostAsJsonAsync($"/api/focus/{started.SessionId}/confirm", new { confirmed = true });
            Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        }

        [Fact]
        public async Task Confirm_WithoutCorrection_KeepsAutoTimeout_ButNowCounts()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, lengthMinutes: 30);
            var started = await StartOkAsync(s.Client, s.TaskId, slot.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(60));
            await RunJobAsync(factory, jobs => jobs.AutoTimeout(slot.Id));

            var confirm = await s.Client.PostAsJsonAsync($"/api/focus/{started.SessionId}/confirm", new { confirmed = true });

            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
            var session = await QueryAsync(factory, db => db.FocusSessions.SingleAsync());
            Assert.True(session.IsConfirmed);
            Assert.Equal("AutoTimeout", session.CaptureMethod);
            Assert.Equal(60, (await SummaryAsync(s.Client, slot.Id)).ActualMinutes);
        }

        [Fact]
        public async Task Confirm_False_DiscardsTheSession_ForGood()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, lengthMinutes: 30);
            var started = await StartOkAsync(s.Client, s.TaskId, slot.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(60));
            await RunJobAsync(factory, jobs => jobs.AutoTimeout(slot.Id));

            var discard = await s.Client.PostAsJsonAsync($"/api/focus/{started.SessionId}/confirm", new { confirmed = false });

            Assert.Equal(HttpStatusCode.OK, discard.StatusCode);
            var session = await QueryAsync(factory, db => db.FocusSessions.SingleAsync());
            Assert.False(session.IsConfirmed);
            Assert.Contains(FocusSessionService.DiscardedMarker, session.Note);
            Assert.Equal(0, (await SummaryAsync(s.Client, slot.Id)).SessionCount);

            // Đã loại bỏ thì sau đó không "xác nhận" ngược lại được
            var revive = await s.Client.PostAsJsonAsync($"/api/focus/{started.SessionId}/confirm", new { confirmed = true });
            Assert.Equal(HttpStatusCode.BadRequest, revive.StatusCode);
        }

        [Fact]
        public async Task Confirm_IsOnlyForUnconfirmedAutoTimeoutSessions_AndOnlyForTheOwner()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory, "owner");
            var (other, _) = await TestHelpers.LoginAsync(factory, "other");
            var started = await StartOkAsync(s.Client, s.TaskId);
            factory.Clock.Advance(TimeSpan.FromMinutes(5));
            await StopOkAsync(s.Client, started.SessionId, "Completed");

            var timer = await s.Client.PostAsJsonAsync($"/api/focus/{started.SessionId}/confirm", new { confirmed = true });
            var stranger = await other.PostAsJsonAsync($"/api/focus/{started.SessionId}/confirm", new { confirmed = true });
            var unknown = await s.Client.PostAsJsonAsync($"/api/focus/{Guid.NewGuid()}/confirm", new { confirmed = true });

            Assert.Equal(HttpStatusCode.BadRequest, timer.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, stranger.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        // ---------------------------------------------------------------- Self-report

        [Fact]
        public async Task SelfReport_CreatesAConfirmedSelfReportedSession_ThatCountsInTheSummary()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, startInMinutes: -120, lengthMinutes: 60);
            var now = factory.Clock.GetUtcNow();

            var response = await s.Client.PostAsJsonAsync("/api/focus/self-report", new
            {
                taskId = s.TaskId,
                scheduleSlotId = slot.Id,
                startedAt = now.AddMinutes(-110),
                endedAt = now.AddMinutes(-65),
                stopReason = "Completed",
                note = "Quên bấm Start"
            });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var session = await QueryAsync(factory, db => db.FocusSessions.SingleAsync());
            Assert.Equal("SelfReported", session.CaptureMethod);
            Assert.True(session.IsConfirmed);
            Assert.Equal("Quên bấm Start", session.Note);

            var summary = await SummaryAsync(s.Client, slot.Id);
            Assert.Equal(45, summary.ActualMinutes);
            Assert.Equal("Done", await SlotStatusAsync(s.Client, slot.Id));
        }

        [Fact]
        public async Task SelfReport_RejectsBadTimes_AndSomeoneElsesTask()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory, "owner");
            var (other, _) = await TestHelpers.LoginAsync(factory, "other");
            var now = factory.Clock.GetUtcNow();

            async Task<HttpStatusCode> Report(HttpClient client, DateTimeOffset start, DateTimeOffset end) =>
                (await client.PostAsJsonAsync("/api/focus/self-report",
                    new { taskId = s.TaskId, startedAt = start, endedAt = end, stopReason = "Completed" })).StatusCode;

            Assert.Equal(HttpStatusCode.BadRequest, await Report(s.Client, now.AddMinutes(-10), now.AddMinutes(-10)));
            Assert.Equal(HttpStatusCode.BadRequest, await Report(s.Client, now.AddMinutes(-10), now.AddMinutes(-20)));
            Assert.Equal(HttpStatusCode.BadRequest, await Report(s.Client, now.AddMinutes(10), now.AddMinutes(20)));
            Assert.Equal(HttpStatusCode.BadRequest, await Report(s.Client, now.AddHours(-30), now.AddHours(-1)));
            Assert.Equal(HttpStatusCode.NotFound, await Report(other, now.AddMinutes(-30), now.AddMinutes(-10)));
            Assert.Equal(0, await QueryAsync(factory, db => db.FocusSessions.CountAsync()));
        }

        // ---------------------------------------------------------------- Time summary

        [Fact]
        public async Task TimeSummary_IsOnlyForTheOwner_AndBothRoutesWork()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory, "owner");
            var (other, _) = await TestHelpers.LoginAsync(factory, "other");
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, 30);

            var viaSlots = await s.Client.GetAsync($"/api/slots/{slot.Id}/time-summary");
            var viaOldRoute = await s.Client.GetAsync($"/api/FocusSession/{slot.Id}/time-summary");
            var stranger = await other.GetAsync($"/api/focus/{slot.Id}/time-summary");

            Assert.Equal(HttpStatusCode.OK, viaSlots.StatusCode);
            Assert.Equal(HttpStatusCode.OK, viaOldRoute.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        }

        // ---------------------------------------------------------------- Background jobs

        [Fact]
        public async Task SlotDue_MovesApprovedToDue_AndNotifiesOnce()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, 30);

            await RunJobAsync(factory, jobs => jobs.SlotDue(slot.Id));
            await RunJobAsync(factory, jobs => jobs.SlotDue(slot.Id));

            Assert.Equal("Due", await SlotStatusAsync(s.Client, slot.Id));
            var notification = Assert.Single(await NotificationsAsync(factory, "START_PROMPT"));
            Assert.Equal(s.UserId, notification.UserId);
            Assert.Equal(slot.Id, notification.ScheduleSlotId);
            Assert.False(notification.IsRead);
        }

        [Fact]
        public async Task Jobs_DoNothing_WhenTheSlotWasCancelledOrAlreadyDone()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, 30);
            Assert.Equal(HttpStatusCode.OK, (await s.Client.DeleteAsync($"/api/schedule-slots/{slot.Id}")).StatusCode);

            await RunJobAsync(factory, jobs => jobs.PreStartReminder(slot.Id));
            await RunJobAsync(factory, jobs => jobs.SlotDue(slot.Id));
            await RunJobAsync(factory, jobs => jobs.NotStartedNudge(slot.Id));
            await RunJobAsync(factory, jobs => jobs.NearEndReminder(slot.Id));
            await RunJobAsync(factory, jobs => jobs.SlotEnded(slot.Id));
            await RunJobAsync(factory, jobs => jobs.OverrunReminder(slot.Id));
            await RunJobAsync(factory, jobs => jobs.AutoTimeout(slot.Id));

            Assert.Equal("Cancelled", await SlotStatusAsync(s.Client, slot.Id));
            Assert.Empty(await NotificationsAsync(factory));
        }

        [Fact]
        public async Task PreStartReminder_OnlyForApprovedSlots()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 15, 30);

            await RunJobAsync(factory, jobs => jobs.PreStartReminder(slot.Id));

            Assert.Single(await NotificationsAsync(factory, "PRE_START_REMINDER"));
            Assert.Equal("Approved", await SlotStatusAsync(s.Client, slot.Id));
        }

        [Fact]
        public async Task NotStartedNudge_OnlyWhileDueAndNothingWasEverStarted()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, 60);
            await RunJobAsync(factory, jobs => jobs.SlotDue(slot.Id));

            await RunJobAsync(factory, jobs => jobs.NotStartedNudge(slot.Id));
            Assert.Single(await NotificationsAsync(factory, "NOT_STARTED"));

            // Đã làm một phiên rồi tạm dừng thì không nhắc "chưa bắt đầu" nữa
            var started = await StartOkAsync(s.Client, s.TaskId, slot.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(5));
            await StopOkAsync(s.Client, started.SessionId, "Interrupted");
            await RunJobAsync(factory, jobs => jobs.NotStartedNudge(slot.Id));
            Assert.Single(await NotificationsAsync(factory, "NOT_STARTED"));
        }

        [Fact]
        public async Task SlotEnded_DecidesBetweenMissed_Overrun_AndCheckIn()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);

            // Chưa từng bấm Start -> Missed
            var missed = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, 30);
            await RunJobAsync(factory, jobs => jobs.SlotDue(missed.Id));
            await RunJobAsync(factory, jobs => jobs.SlotEnded(missed.Id));
            Assert.Equal("Missed", await SlotStatusAsync(s.Client, missed.Id));
            var missedNotice = Assert.Single(await NotificationsAsync(factory, "MISSED_SLOT"));
            Assert.Equal("MissedSlot", missedNotice.Type);

            // Đang làm -> Overrun (phiên vẫn chạy)
            var running = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, 30);
            var started = await StartOkAsync(s.Client, s.TaskId, running.Id);
            await RunJobAsync(factory, jobs => jobs.SlotEnded(running.Id));
            Assert.Equal("Overrun", await SlotStatusAsync(s.Client, running.Id));
            Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/focus/current")).StatusCode);

            // Nhắc quá giờ chỉ khi còn phiên đang chạy
            await RunJobAsync(factory, jobs => jobs.OverrunReminder(running.Id));
            Assert.Single(await NotificationsAsync(factory, "OVERRUN"));
            factory.Clock.Advance(TimeSpan.FromMinutes(40));
            await StopOkAsync(s.Client, started.SessionId, "Completed");

            // Đã làm một phần rồi tạm dừng, hết giờ -> hỏi lại (CHECK_IN), không bị coi là Missed
            var paused = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, 30);
            var pausedSession = await StartOkAsync(s.Client, s.TaskId, paused.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(10));
            await StopOkAsync(s.Client, pausedSession.SessionId, "Interrupted");
            await RunJobAsync(factory, jobs => jobs.SlotEnded(paused.Id));
            Assert.Equal("Due", await SlotStatusAsync(s.Client, paused.Id));
            Assert.Single(await NotificationsAsync(factory, "CHECK_IN"));
        }

        [Fact]
        public async Task AutoTimeout_DoesNothing_WhenTheSessionWasAlreadyStopped()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, 30);
            var started = await StartOkAsync(s.Client, s.TaskId, slot.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(20));
            await StopOkAsync(s.Client, started.SessionId, "Interrupted");

            factory.Clock.Advance(TimeSpan.FromMinutes(60));
            await RunJobAsync(factory, jobs => jobs.AutoTimeout(slot.Id));

            var session = await QueryAsync(factory, db => db.FocusSessions.SingleAsync());
            Assert.Equal("Timer", session.CaptureMethod);
            Assert.True(session.IsConfirmed);
            Assert.Empty(await NotificationsAsync(factory, "AUTO_TIMEOUT"));
        }

        [Fact]
        public async Task EveryNotificationTheJobsSend_IsStoredWithATypeTheDatabaseCheckAllows()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 15, 30);

            // Chạy mọi job có thể gửi thông báo, ở trạng thái slot phù hợp với từng job
            await RunJobAsync(factory, jobs => jobs.PreStartReminder(slot.Id));
            await RunJobAsync(factory, jobs => jobs.SlotDue(slot.Id));
            await RunJobAsync(factory, jobs => jobs.NotStartedNudge(slot.Id));
            var started = await StartOkAsync(s.Client, s.TaskId, slot.Id);
            await RunJobAsync(factory, jobs => jobs.NearEndReminder(slot.Id));
            await RunJobAsync(factory, jobs => jobs.SlotEnded(slot.Id));
            await RunJobAsync(factory, jobs => jobs.OverrunReminder(slot.Id));
            factory.Clock.Advance(TimeSpan.FromMinutes(70));
            await RunJobAsync(factory, jobs => jobs.AutoTimeout(slot.Id));
            var missed = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, 30);
            await RunJobAsync(factory, jobs => jobs.SlotDue(missed.Id));
            await RunJobAsync(factory, jobs => jobs.SlotEnded(missed.Id));

            // Trước đây ghi START_PROMPT, NEAR_END... thẳng vào cột Type nên Postgres từ chối và không có thông báo nào được lưu
            var all = await NotificationsAsync(factory);
            Assert.True(all.Count >= 7, $"chỉ có {all.Count} thông báo");
            Assert.All(all, n => Assert.Contains(n.Type, AllowedStoredTypes));
            Assert.Contains(all, n => n.Type == "MissedSlot");
            Assert.NotNull(started);
        }

        // ---------------------------------------------------------------- Jobs follow the slot

        [Fact]
        public async Task Slots_KeepTheirJobsInSyncWithTheirState()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);

            // Đã duyệt thì hẹn job; chưa duyệt (Proposed) thì không hẹn gì
            var approved = await CreateSlotAsync(factory, s.Client, s.TaskId, 30, 60);
            var proposed = await CreateSlotAsync(factory, s.Client, s.TaskId, 30, 60, status: "Proposed");
            Assert.Single(factory.Jobs.CallsFor("ScheduleAllJobsForSlot", approved.Id));
            Assert.Empty(factory.Jobs.CallsFor("ScheduleAllJobsForSlot", proposed.Id));

            // Dời giờ (kéo thả, đồng bộ Google, lập lại lịch) thì hẹn lại toàn bộ theo giờ mới
            var moved = await s.Client.PutAsJsonAsync($"/api/schedule-slots/{approved.Id}", new ScheduleSlotRequestDto
            {
                StartTime = approved.StartTime.AddHours(3),
                EndTime = approved.EndTime.AddHours(3),
                IsManualOverride = true,
                Status = "Approved"
            });
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
            Assert.Equal(2, factory.Jobs.CallsFor("ScheduleAllJobsForSlot", approved.Id).Count());

            // Bỏ qua (Skipped) hoặc hủy thì hủy hết job
            var skipped = await s.Client.PutAsJsonAsync($"/api/schedule-slots/{approved.Id}", new ScheduleSlotRequestDto
            {
                StartTime = approved.StartTime.AddHours(3),
                EndTime = approved.EndTime.AddHours(3),
                IsManualOverride = true,
                Status = "Skipped"
            });
            Assert.Equal(HttpStatusCode.OK, skipped.StatusCode);
            Assert.NotEmpty(factory.Jobs.CallsFor("CancelAllJobs", approved.Id));

            Assert.Equal(HttpStatusCode.OK, (await s.Client.DeleteAsync($"/api/schedule-slots/{proposed.Id}")).StatusCode);
            Assert.NotEmpty(factory.Jobs.CallsFor("CancelAllJobs", proposed.Id));
        }

        // ---------------------------------------------------------------- Hợp đồng JSON với frontend

        [Fact]
        public async Task Responses_UseCamelCaseNames_AndEnumsAsStrings()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, 30);

            var start = JsonDocument.Parse(await (await StartAsync(s.Client, s.TaskId, slot.Id)).Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("Timer", start.GetProperty("captureMethod").GetString());
            foreach (var name in new[] { "sessionId", "startedAt", "plannedMinutes", "expectedEndAt", "startedEarlyMinutes" })
                Assert.True(start.TryGetProperty(name, out _), $"thiếu {name} trong phản hồi start");

            factory.Clock.Advance(TimeSpan.FromSeconds(90));
            var current = JsonDocument.Parse(await s.Client.GetStringAsync("/api/focus/current")).RootElement;
            foreach (var name in new[] { "sessionId", "taskId", "taskTitle", "scheduleSlotId", "startedAt", "elapsedSeconds", "serverTime", "isOverrun" })
                Assert.True(current.TryGetProperty(name, out _), $"thiếu {name} trong phản hồi current");

            var stop = JsonDocument.Parse(await (await StopAsync(s.Client, start.GetProperty("sessionId").GetGuid(), "Completed"))
                .Content.ReadAsStringAsync()).RootElement;
            foreach (var name in new[] { "sessionId", "endedAt", "sessionMinutes", "taskTotalMinutes", "checkInRequired" })
                Assert.True(stop.TryGetProperty(name, out _), $"thiếu {name} trong phản hồi stop");

            var summary = JsonDocument.Parse(await s.Client.GetStringAsync($"/api/focus/{slot.Id}/time-summary")).RootElement;
            var first = summary.GetProperty("sessions")[0];
            Assert.Equal("Timer", first.GetProperty("captureMethod").GetString());
            Assert.Equal("Completed", first.GetProperty("stopReason").GetString());
        }

        // ---------------------------------------------------------------- Notification inbox

        [Fact]
        public async Task Inbox_ListsMissedNotifications_AndTracksReadState()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory, "owner");
            var (other, _) = await TestHelpers.LoginAsync(factory, "other");
            var slot = await CreateSlotAsync(factory, s.Client, s.TaskId, 0, 30);
            await RunJobAsync(factory, jobs => jobs.PreStartReminder(slot.Id));
            await RunJobAsync(factory, jobs => jobs.SlotDue(slot.Id));

            var all = (await s.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!;
            Assert.Equal(2, all.Count);
            Assert.Equal("Đến giờ làm việc", all[0].Title);
            Assert.All(all, n => Assert.Equal("SlotReminder", n.Type));
            Assert.All(all, n => Assert.False(n.IsRead));
            Assert.Empty((await other.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!);

            Assert.Equal(HttpStatusCode.NoContent, (await s.Client.PostAsync($"/api/notifications/{all[0].Id}/read", null)).StatusCode);
            var unread = (await s.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications?unreadOnly=true"))!;
            Assert.Single(unread);
            Assert.Equal(all[1].Id, unread[0].Id);

            // Không đánh dấu hộ thông báo của người khác
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/notifications/{all[1].Id}/read", null)).StatusCode);

            var readAll = await s.Client.PostAsync("/api/notifications/read-all", null);
            Assert.Equal(HttpStatusCode.OK, readAll.StatusCode);
            Assert.Empty((await s.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications?unreadOnly=true"))!);
            Assert.Equal(2, (await s.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!.Count);
        }

        [Fact]
        public async Task Notifications_RequireLogin_AndTheVapidPublicKeyIsExposed()
        {
            using var factory = new ApiFactory();
            var anonymous = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/notifications")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/notifications/vapid-public-key")).StatusCode);

            var (client, _) = await TestHelpers.LoginAsync(factory);
            var key = await client.GetAsync("/api/notifications/vapid-public-key");

            Assert.Equal(HttpStatusCode.OK, key.StatusCode);
            using var body = JsonDocument.Parse(await key.Content.ReadAsStringAsync());
            Assert.Equal(ApiFactory.VapidPublicKey, body.RootElement.GetProperty("publicKey").GetString());
            Assert.DoesNotContain("private", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SignalRHub_AcceptsTheJwtOnTheQueryString_AndRejectsAnonymousClients()
        {
            using var factory = new ApiFactory();
            var (_, auth) = await TestHelpers.LoginAsync(factory);
            var anonymous = factory.CreateClient();

            var denied = await anonymous.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null);
            // Trình duyệt không gắn được header Authorization cho WebSocket nên SignalR gửi token trên query string
            var allowed = await anonymous.PostAsync(
                $"/hubs/notifications/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(auth.AccessToken)}", null);

            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.Contains("connectionId", await allowed.Content.ReadAsStringAsync());
        }
    }
}
