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
    // Luồng trạng thái liền mạch của Milestone, Task, SubTask, ScheduleSlot và FocusSession (xem docs/STATUS_WORKFLOW.md)
    public class StatusWorkflowTests
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private sealed record Setup(HttpClient Client, Guid ProjectId, Guid DomainId, Guid UserId);

        private static async Task<Setup> SetupAsync(ApiFactory factory, string subject = "user-1")
        {
            var domainId = Guid.NewGuid();
            await factory.SeedAsync(db => db.Domains.Add(new DomainEntities.Domain { Id = domainId, Name = $"Dev-{Guid.NewGuid()}", IsActive = true }));
            var (client, auth) = await TestHelpers.LoginAsync(factory, subject);
            var project = await (await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Website" }))
                .Content.ReadFromJsonAsync<ProjectDto>();
            return new Setup(client, project!.Id, domainId, auth.UserId);
        }

        private static async Task<MilestoneDto> NewMilestoneAsync(Setup s, int order = 1, string status = "Planned")
        {
            var response = await s.Client.PostAsJsonAsync($"/api/projects/{s.ProjectId}/milestones", MilestoneBody(order, status));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<MilestoneDto>())!;
        }

        private static MilestoneRequestDto MilestoneBody(int order, string? status) => new()
        {
            Name = $"Giai đoạn {order}",
            OrderIndex = order,
            DueDate = new DateOnly(2030, 1, 1),
            Status = status,
            RevisionCount = 0
        };

        private static Task<HttpResponseMessage> PutMilestoneAsync(Setup s, Guid id, int order, string? status) =>
            s.Client.PutAsJsonAsync($"/api/milestones/{id}", MilestoneBody(order, status));

        private static async Task<TaskDto> NewTaskAsync(Setup s, string title = "Task cha", Guid? milestoneId = null, string? status = null)
        {
            var response = await s.Client.PostAsJsonAsync($"/api/projects/{s.ProjectId}/tasks", new TaskRequestDto
            {
                Title = title,
                TaskType = "Flexible",
                DomainId = s.DomainId,
                MilestoneId = milestoneId,
                Status = status,
                Deadline = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                EstimatedMinutes = 60
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<TaskDto>())!;
        }

        // PUT task thay thế toàn bộ: lấy task hiện tại, gửi lại đủ trường, chỉ đổi status (và milestone nếu truyền)
        private static async Task<HttpResponseMessage> PutTaskAsync(Setup s, Guid taskId, string? status, Guid? milestoneId = null, bool changeMilestone = false)
        {
            var t = (await s.Client.GetFromJsonAsync<TaskDto>($"/api/tasks/{taskId}"))!;
            return await s.Client.PutAsJsonAsync($"/api/tasks/{taskId}", new TaskRequestDto
            {
                Title = t.Title,
                TaskType = t.TaskType,
                DomainId = t.DomainId,
                MilestoneId = changeMilestone ? milestoneId : t.MilestoneId,
                Status = status,
                Deadline = t.Deadline,
                EstimatedMinutes = t.EstimatedMinutes
            });
        }

        private static async Task<string> TaskStatusAsync(Setup s, Guid taskId) =>
            (await s.Client.GetFromJsonAsync<TaskDto>($"/api/tasks/{taskId}"))!.Status;

        private static async Task<SubTaskDto> NewSubTaskAsync(Setup s, Guid taskId, int order, string? status = null)
        {
            var response = await s.Client.PostAsJsonAsync($"/api/tasks/{taskId}/subtasks",
                new SubTaskRequestDto { Title = $"Subtask {order}", EstimatedMinutes = 20, OrderIndex = order, Status = status });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<SubTaskDto>())!;
        }

        private static Task<HttpResponseMessage> PutSubTaskAsync(Setup s, SubTaskDto sub, string? status, string? title = null) =>
            s.Client.PutAsJsonAsync($"/api/subtasks/{sub.Id}", new SubTaskRequestDto
            { Title = title ?? sub.Title, EstimatedMinutes = sub.EstimatedMinutes, OrderIndex = sub.OrderIndex, Status = status });

        private static async Task<string> SubTaskStatusAsync(Setup s, Guid id) =>
            (await s.Client.GetFromJsonAsync<SubTaskDto>($"/api/subtasks/{id}"))!.Status;

        private static async Task<ScheduleSlotDto> NewSlotAsync(ApiFactory f, Setup s, Guid taskId, Guid? subTaskId = null, int startIn = 0, int length = 30, string status = "Approved")
        {
            var start = f.Clock.GetUtcNow().AddMinutes(startIn);
            var response = await s.Client.PostAsJsonAsync($"/api/tasks/{taskId}/schedule-slots", new ScheduleSlotRequestDto
            {
                SubTaskId = subTaskId, StartTime = start, EndTime = start.AddMinutes(length), IsManualOverride = true, Status = status
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ScheduleSlotDto>())!;
        }

        private static Task<HttpResponseMessage> PutSlotStatusAsync(Setup s, ScheduleSlotDto slot, string status) =>
            s.Client.PutAsJsonAsync($"/api/schedule-slots/{slot.Id}", new ScheduleSlotRequestDto
            { SubTaskId = slot.SubTaskId, StartTime = slot.StartTime, EndTime = slot.EndTime, IsManualOverride = true, Status = status });

        private static async Task<string> SlotStatusAsync(Setup s, Guid slotId) =>
            (await s.Client.GetFromJsonAsync<ScheduleSlotDto>($"/api/schedule-slots/{slotId}"))!.Status;

        private static Task<HttpResponseMessage> StartAsync(Setup s, Guid taskId, Guid? slotId = null, Guid? subTaskId = null) =>
            s.Client.PostAsJsonAsync("/api/focus/start", new { taskId, scheduleSlotId = slotId, subTaskId });

        private static async Task<Guid> StartOkAsync(Setup s, Guid taskId, Guid? slotId = null, Guid? subTaskId = null)
        {
            var response = await StartAsync(s, taskId, slotId, subTaskId);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<FocusStartResponse>(Json))!.SessionId;
        }

        private static Task<HttpResponseMessage> StopAsync(Setup s, Guid sessionId, string reason, string? note = null) =>
            s.Client.PostAsJsonAsync($"/api/focus/{sessionId}/stop", new { stopReason = reason, note });

        private static async Task StopOkAsync(Setup s, Guid sessionId, string reason, string? note = null)
        {
            var response = await StopAsync(s, sessionId, reason, note);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private static async Task RunJobAsync(ApiFactory f, Func<SlotBackgroundJobs, Task> job)
        {
            using var scope = f.Services.CreateScope();
            await job(scope.ServiceProvider.GetRequiredService<SlotBackgroundJobs>());
        }

        private static async Task<List<FocusSessionListItem>> SessionsAsync(Setup s, Guid taskId) =>
            (await s.Client.GetFromJsonAsync<List<FocusSessionListItem>>($"/api/focus/task/{taskId}/sessions", Json))!;

        // ---------------------------------------------------------------- SubTask

        [Fact]
        public async Task SubTask_FollowsItsTransitionTable()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var sub = await NewSubTaskAsync(s, task.Id, 1);
            await NewSubTaskAsync(s, task.Id, 2); // subtask thứ hai còn ToDo để Task không tự Done

            Assert.Equal(HttpStatusCode.OK, (await PutSubTaskAsync(s, sub, "Done")).StatusCode);
            // Done và Skipped không đi thẳng sang nhau: phải qua ToDo
            var direct = await PutSubTaskAsync(s, sub, "Skipped");
            Assert.Equal(HttpStatusCode.BadRequest, direct.StatusCode);
            Assert.Contains("ToDo", await direct.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await PutSubTaskAsync(s, sub, "ToDo")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PutSubTaskAsync(s, sub, "Skipped")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await PutSubTaskAsync(s, sub, "Done")).StatusCode);
            Assert.Equal("Skipped", await SubTaskStatusAsync(s, sub.Id));
        }

        [Fact]
        public async Task SubTask_PutWithoutStatus_KeepsTheCurrentStatus()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var sub = await NewSubTaskAsync(s, task.Id, 1);
            await NewSubTaskAsync(s, task.Id, 2);
            await PutSubTaskAsync(s, sub, "Done");

            // Đổi tên mà không gửi status: trước đây tự về ToDo làm mở lại subtask đã xong
            Assert.Equal(HttpStatusCode.OK, (await PutSubTaskAsync(s, sub, null, "Tên mới")).StatusCode);

            Assert.Equal("Done", await SubTaskStatusAsync(s, sub.Id));
        }

        // ---------------------------------------------------------------- Task: bảng chuyển và suy ra từ sự thật

        [Fact]
        public async Task Task_DoneAndSkipped_AreFinal_UntilReopened()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);

            Assert.Equal(HttpStatusCode.OK, (await PutTaskAsync(s, task.Id, "Done")).StatusCode);
            Assert.NotNull((await s.Client.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}"))!.CompletedAt);

            var direct = await PutTaskAsync(s, task.Id, "Skipped");
            Assert.Equal(HttpStatusCode.BadRequest, direct.StatusCode);
            Assert.Contains("ToDo", await direct.Content.ReadAsStringAsync());

            // Mở lại: xóa CompletedAt
            Assert.Equal(HttpStatusCode.OK, (await PutTaskAsync(s, task.Id, "ToDo")).StatusCode);
            var reopened = (await s.Client.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}"))!;
            Assert.Equal("ToDo", reopened.Status);
            Assert.Null(reopened.CompletedAt);

            Assert.Equal(HttpStatusCode.OK, (await PutTaskAsync(s, task.Id, "Skipped")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await PutTaskAsync(s, task.Id, "Done")).StatusCode);
        }

        [Fact]
        public async Task Task_PutWithoutStatus_KeepsTheCurrentStatus()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            await PutTaskAsync(s, task.Id, "Done");

            Assert.Equal(HttpStatusCode.OK, (await PutTaskAsync(s, task.Id, null)).StatusCode);

            Assert.Equal("Done", await TaskStatusAsync(s, task.Id));
        }

        [Fact]
        public async Task Task_BecomesScheduled_WhenAnApprovedSlotExists_AndBackToToDoWhenItIsCancelled()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);

            // Slot chưa duyệt chưa làm Task thành Scheduled
            var proposed = await NewSlotAsync(factory, s, task.Id, status: "Proposed");
            Assert.Equal("ToDo", await TaskStatusAsync(s, task.Id));

            Assert.Equal(HttpStatusCode.OK, (await PutSlotStatusAsync(s, proposed, "Approved")).StatusCode);
            Assert.Equal("Scheduled", await TaskStatusAsync(s, task.Id));

            Assert.Equal(HttpStatusCode.OK, (await s.Client.DeleteAsync($"/api/schedule-slots/{proposed.Id}")).StatusCode);
            Assert.Equal("ToDo", await TaskStatusAsync(s, task.Id));
        }

        [Fact]
        public async Task Task_BecomesInProgress_WhenASessionStarts_AndStaysSoAfterAnInterruption()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var slot = await NewSlotAsync(factory, s, task.Id);
            Assert.Equal("Scheduled", await TaskStatusAsync(s, task.Id));

            var session = await StartOkAsync(s, task.Id, slot.Id);
            Assert.Equal("InProgress", await TaskStatusAsync(s, task.Id));

            factory.Clock.Advance(TimeSpan.FromMinutes(10));
            await StopOkAsync(s, session, "Interrupted");
            Assert.Equal("InProgress", await TaskStatusAsync(s, task.Id));
        }

        [Fact]
        public async Task Task_CannotBeClosed_WhileASessionIsRunning()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var session = await StartOkAsync(s, task.Id);

            var response = await PutTaskAsync(s, task.Id, "Done");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("InProgress", await TaskStatusAsync(s, task.Id));
            await StopOkAsync(s, session, "Completed");
            Assert.Equal(HttpStatusCode.OK, (await PutTaskAsync(s, task.Id, "Done")).StatusCode);
        }

        [Fact]
        public async Task ClosingATask_SkipsItsOpenSubTasks_AndCancelsSlotsThatHaveNotHappened()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var done = await NewSubTaskAsync(s, task.Id, 1);
            var open1 = await NewSubTaskAsync(s, task.Id, 2);
            var open2 = await NewSubTaskAsync(s, task.Id, 3);
            await PutSubTaskAsync(s, done, "Done");
            var approved = await NewSlotAsync(factory, s, task.Id, open1.Id, startIn: 60);
            var proposed = await NewSlotAsync(factory, s, task.Id, open2.Id, startIn: 120, status: "Proposed");

            var response = await PutTaskAsync(s, task.Id, "Done");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Done", await SubTaskStatusAsync(s, done.Id));
            Assert.Equal("Skipped", await SubTaskStatusAsync(s, open1.Id));
            Assert.Equal("Skipped", await SubTaskStatusAsync(s, open2.Id));
            Assert.Equal("Cancelled", await SlotStatusAsync(s, approved.Id));
            Assert.Equal("Cancelled", await SlotStatusAsync(s, proposed.Id));
            Assert.NotEmpty(factory.Jobs.CallsFor("CancelAllJobs", approved.Id));
        }

        [Fact]
        public async Task Task_AutoCompletes_WhenEverySubTaskIsHandled_AndReopensWhenOneComesBack()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var a = await NewSubTaskAsync(s, task.Id, 1);
            var b = await NewSubTaskAsync(s, task.Id, 2);

            await PutSubTaskAsync(s, a, "Done");
            Assert.Equal("InProgress", await TaskStatusAsync(s, task.Id));   // đã có subtask xong, chưa hết

            await PutSubTaskAsync(s, b, "Skipped");                          // Done + Skipped, không còn ToDo
            var done = (await s.Client.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}"))!;
            Assert.Equal("Done", done.Status);
            Assert.NotNull(done.CompletedAt);

            // Mở lại một subtask: Task đã Done phải mở lại
            await PutSubTaskAsync(s, b, "ToDo");
            var reopened = (await s.Client.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}"))!;
            Assert.Equal("InProgress", reopened.Status);
            Assert.Null(reopened.CompletedAt);

            // Thêm subtask mới vào Task đã Done cũng mở lại
            await PutSubTaskAsync(s, b, "Done");
            Assert.Equal("Done", await TaskStatusAsync(s, task.Id));
            await NewSubTaskAsync(s, task.Id, 3);
            Assert.Equal("InProgress", await TaskStatusAsync(s, task.Id));
        }

        [Fact]
        public async Task Task_DoesNotAutoComplete_WhenNoSubTaskWasActuallyDone()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var a = await NewSubTaskAsync(s, task.Id, 1);
            var b = await NewSubTaskAsync(s, task.Id, 2);

            await PutSubTaskAsync(s, a, "Skipped");
            await PutSubTaskAsync(s, b, "Skipped");

            Assert.NotEqual("Done", await TaskStatusAsync(s, task.Id));
        }

        [Fact]
        public async Task ClosedTasks_CannotGetNewSlots_NorBeStarted()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var sub = await NewSubTaskAsync(s, task.Id, 1);
            var other = await NewSubTaskAsync(s, task.Id, 2);
            await PutSubTaskAsync(s, sub, "Done");

            // Subtask đã Done không bắt đầu lại được (phải mở lại)
            Assert.Equal(HttpStatusCode.BadRequest, (await StartAsync(s, task.Id, subTaskId: sub.Id)).StatusCode);

            await PutTaskAsync(s, task.Id, "Done");
            var slot = await s.Client.PostAsJsonAsync($"/api/tasks/{task.Id}/schedule-slots", new ScheduleSlotRequestDto
            {
                StartTime = factory.Clock.GetUtcNow(), EndTime = factory.Clock.GetUtcNow().AddMinutes(30), Status = "Approved"
            });
            Assert.Equal(HttpStatusCode.BadRequest, slot.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await StartAsync(s, task.Id)).StatusCode);
            Assert.NotNull(other);
        }

        // ---------------------------------------------------------------- Slot: bảng chuyển

        [Fact]
        public async Task Slot_FollowsItsTransitionTable()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var slot = await NewSlotAsync(factory, s, task.Id, status: "Proposed");

            Assert.Equal(HttpStatusCode.BadRequest, (await PutSlotStatusAsync(s, slot, "Done")).StatusCode);        // Proposed không nhảy thẳng sang Done
            Assert.Equal(HttpStatusCode.OK, (await PutSlotStatusAsync(s, slot, "Approved")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PutSlotStatusAsync(s, slot, "Postponed")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PutSlotStatusAsync(s, slot, "Approved")).StatusCode);            // xếp lại
            Assert.Equal(HttpStatusCode.OK, (await PutSlotStatusAsync(s, slot, "Done")).StatusCode);

            // Done là trạng thái chốt
            var back = await PutSlotStatusAsync(s, slot, "Proposed");
            Assert.Equal(HttpStatusCode.BadRequest, back.StatusCode);
            using var body = JsonDocument.Parse(await back.Content.ReadAsStringAsync());
            Assert.Contains("không còn bước nào", body.RootElement.GetProperty("message").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.DeleteAsync($"/api/schedule-slots/{slot.Id}")).StatusCode);

            var second = await NewSlotAsync(factory, s, task.Id);
            Assert.Equal(HttpStatusCode.OK, (await s.Client.DeleteAsync($"/api/schedule-slots/{second.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await PutSlotStatusAsync(s, second, "Approved")).StatusCode);   // Cancelled là chốt
        }

        [Fact]
        public async Task NewSlots_CanOnlyStartAsProposedOrApproved()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var start = factory.Clock.GetUtcNow();

            var response = await s.Client.PostAsJsonAsync($"/api/tasks/{task.Id}/schedule-slots", new ScheduleSlotRequestDto
            { StartTime = start, EndTime = start.AddMinutes(30), Status = "Done" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // ---------------------------------------------------------------- StopReason

        [Fact]
        public async Task StopReason_Completed_MakesTheSlotDone_AndCancelsAllItsJobs()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var slot = await NewSlotAsync(factory, s, task.Id);
            var session = await StartOkAsync(s, task.Id, slot.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(20));

            await StopOkAsync(s, session, "Completed");

            Assert.Equal("Done", await SlotStatusAsync(s, slot.Id));
            Assert.NotEmpty(factory.Jobs.CallsFor("CancelAllJobs", slot.Id));
            // Task không có subtask: Stop Completed không tự đóng task
            Assert.Equal("InProgress", await TaskStatusAsync(s, task.Id));
        }

        [Fact]
        public async Task StopReason_Interrupted_And_Other_SendTheSlotBackToDue()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var slot = await NewSlotAsync(factory, s, task.Id);

            var first = await StartOkAsync(s, task.Id, slot.Id);
            await StopOkAsync(s, first, "Interrupted");                       // note tùy chọn
            Assert.Equal("Due", await SlotStatusAsync(s, slot.Id));

            var second = await StartOkAsync(s, task.Id, slot.Id);
            await StopOkAsync(s, second, "Other", "Khách gọi điện đột xuất");  // Other bắt buộc note
            Assert.Equal("Due", await SlotStatusAsync(s, slot.Id));
            Assert.Empty(factory.Jobs.CallsFor("CancelAllJobs", slot.Id));     // vẫn làm tiếp được nên còn nhắc
        }

        [Fact]
        public async Task StopReason_Blocked_PostponesTheSlot_AndItCanBeStartedAgain()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var slot = await NewSlotAsync(factory, s, task.Id);
            var session = await StartOkAsync(s, task.Id, slot.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(5));

            await StopOkAsync(s, session, "Blocked", "Chờ khách gửi tài liệu");

            Assert.Equal("Postponed", await SlotStatusAsync(s, slot.Id));
            Assert.NotEmpty(factory.Jobs.CallsFor("CancelAllJobs", slot.Id));   // đang chờ: hết nhắc "bấm Start"
            Assert.Equal("InProgress", await TaskStatusAsync(s, task.Id));       // đã làm một phần

            // Hết bị kẹt thì làm tiếp được ngay
            var again = await StartOkAsync(s, task.Id, slot.Id);
            Assert.Equal("InProgress", await SlotStatusAsync(s, slot.Id));
            await StopOkAsync(s, again, "Completed");
            Assert.Equal("Done", await SlotStatusAsync(s, slot.Id));
        }

        [Theory]
        [InlineData("Blocked")]
        [InlineData("Other")]
        public async Task StopReason_BlockedAndOther_RequireANote(string reason)
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var slot = await NewSlotAsync(factory, s, task.Id);
            var session = await StartOkAsync(s, task.Id, slot.Id);

            var withoutNote = await StopAsync(s, session, reason);
            var blankNote = await StopAsync(s, session, reason, "   ");

            Assert.Equal(HttpStatusCode.BadRequest, withoutNote.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, blankNote.StatusCode);
            // Phiên vẫn đang chạy, slot không đổi
            Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/focus/current")).StatusCode);
            Assert.Equal("InProgress", await SlotStatusAsync(s, slot.Id));
        }

        [Fact]
        public async Task StopReason_CompletedAndInterrupted_DoNotNeedANote()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);

            var first = await StartOkAsync(s, task.Id);
            await StopOkAsync(s, first, "Interrupted");
            var second = await StartOkAsync(s, task.Id);
            await StopOkAsync(s, second, "Completed");
        }

        [Fact]
        public async Task SelfReport_BlockedAndOther_AlsoRequireANote()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var now = factory.Clock.GetUtcNow();

            var response = await s.Client.PostAsJsonAsync("/api/focus/self-report", new
            { taskId = task.Id, startedAt = now.AddMinutes(-60), endedAt = now.AddMinutes(-30), stopReason = "Blocked" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await SessionsAsync(s, task.Id));
        }

        // ---------------------------------------------------------------- Subtask và Task qua phiên làm việc

        [Fact]
        public async Task StopCompleted_OnASubTaskSession_MakesTheSubTaskDone_AndTheLastOneClosesTheTask()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var a = await NewSubTaskAsync(s, task.Id, 1);
            var b = await NewSubTaskAsync(s, task.Id, 2);
            var slotA = await NewSlotAsync(factory, s, task.Id, a.Id);
            var slotB = await NewSlotAsync(factory, s, task.Id, b.Id);

            var first = await StartOkAsync(s, task.Id, slotA.Id, a.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(10));
            await StopOkAsync(s, first, "Completed");

            Assert.Equal("Done", await SubTaskStatusAsync(s, a.Id));
            Assert.Equal("Done", await SlotStatusAsync(s, slotA.Id));
            Assert.Equal("Approved", await SlotStatusAsync(s, slotB.Id));       // slot của subtask kia không đổi
            Assert.Equal("ToDo", await SubTaskStatusAsync(s, b.Id));
            Assert.Equal("InProgress", await TaskStatusAsync(s, task.Id));       // còn subtask B nên chưa Done

            var second = await StartOkAsync(s, task.Id, slotB.Id, b.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(10));
            await StopOkAsync(s, second, "Completed");

            Assert.Equal("Done", await SubTaskStatusAsync(s, b.Id));
            var finished = (await s.Client.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}"))!;
            Assert.Equal("Done", finished.Status);                                // subtask cuối xong: Task tự Done
            Assert.NotNull(finished.CompletedAt);
        }

        [Fact]
        public async Task StopInterrupted_OnASubTaskSession_LeavesTheSubTaskToDo()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var a = await NewSubTaskAsync(s, task.Id, 1);

            var session = await StartOkAsync(s, task.Id, subTaskId: a.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(5));
            await StopOkAsync(s, session, "Interrupted");

            Assert.Equal("ToDo", await SubTaskStatusAsync(s, a.Id));
            Assert.Equal("InProgress", await TaskStatusAsync(s, task.Id));
        }

        // ---------------------------------------------------------------- CaptureMethod

        [Fact]
        public async Task AutoTimeout_MakesTheSlotDone_ButDoesNotCountAsProgressUntilConfirmed()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var slot = await NewSlotAsync(factory, s, task.Id, length: 30);
            var session = await StartOkAsync(s, task.Id, slot.Id);
            Assert.Equal("InProgress", await TaskStatusAsync(s, task.Id));

            factory.Clock.Advance(TimeSpan.FromMinutes(61));
            await RunJobAsync(factory, jobs => jobs.AutoTimeout(slot.Id));

            Assert.Equal("Done", await SlotStatusAsync(s, slot.Id));
            var item = Assert.Single(await SessionsAsync(s, task.Id));
            Assert.Equal("PendingConfirmation", item.State);
            Assert.Equal(METANOIA.Domain.Enums.CaptureMethod.AutoTimeout, item.CaptureMethod);
            Assert.False(item.IsConfirmed);
            // Chưa xác nhận thì không tính là "đã làm": Task không còn InProgress
            Assert.NotEqual("InProgress", await TaskStatusAsync(s, task.Id));

            // Xác nhận thì tính, Task lên InProgress
            var confirm = await s.Client.PostAsJsonAsync($"/api/focus/{session}/confirm", new { confirmed = true });
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
            Assert.Equal("Confirmed", Assert.Single(await SessionsAsync(s, task.Id)).State);
            Assert.Equal("InProgress", await TaskStatusAsync(s, task.Id));
            Assert.Equal("Done", await SlotStatusAsync(s, slot.Id));
        }

        [Fact]
        public async Task DiscardingAnAutoTimeoutSession_SendsTheSlotToMissed_WhenNothingTrustworthyIsLeft()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var slot = await NewSlotAsync(factory, s, task.Id, length: 30);
            var session = await StartOkAsync(s, task.Id, slot.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(61));
            await RunJobAsync(factory, jobs => jobs.AutoTimeout(slot.Id));
            Assert.Equal("Done", await SlotStatusAsync(s, slot.Id));

            var discard = await s.Client.PostAsJsonAsync($"/api/focus/{session}/confirm", new { confirmed = false });

            Assert.Equal(HttpStatusCode.OK, discard.StatusCode);
            Assert.Equal("Missed", await SlotStatusAsync(s, slot.Id));
            Assert.Equal("Discarded", Assert.Single(await SessionsAsync(s, task.Id)).State);
            Assert.NotEqual("InProgress", await TaskStatusAsync(s, task.Id));
        }

        [Fact]
        public async Task DiscardingAnAutoTimeoutSession_KeepsTheSlotDone_WhenAnotherTrustworthySessionExists()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var slot = await NewSlotAsync(factory, s, task.Id, length: 30);

            var first = await StartOkAsync(s, task.Id, slot.Id);                // phiên Timer đáng tin, bị gián đoạn
            factory.Clock.Advance(TimeSpan.FromMinutes(10));
            await StopOkAsync(s, first, "Interrupted");
            var second = await StartOkAsync(s, task.Id, slot.Id);               // phiên này quên Stop
            factory.Clock.Advance(TimeSpan.FromMinutes(61));
            await RunJobAsync(factory, jobs => jobs.AutoTimeout(slot.Id));

            await s.Client.PostAsJsonAsync($"/api/focus/{second}/confirm", new { confirmed = false });

            Assert.Equal("Done", await SlotStatusAsync(s, slot.Id));
        }

        [Fact]
        public async Task ConfirmWithACorrection_TurnsTheSessionIntoSelfReported()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var slot = await NewSlotAsync(factory, s, task.Id, length: 30);
            var session = await StartOkAsync(s, task.Id, slot.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(61));
            await RunJobAsync(factory, jobs => jobs.AutoTimeout(slot.Id));

            await s.Client.PostAsJsonAsync($"/api/focus/{session}/confirm", new { confirmed = true, correctedMinutes = 25 });

            var item = Assert.Single(await SessionsAsync(s, task.Id));
            Assert.Equal(METANOIA.Domain.Enums.CaptureMethod.SelfReported, item.CaptureMethod);
            Assert.Equal("Stopped", item.State);
            Assert.Equal(25, item.Minutes);
        }

        [Fact]
        public async Task SelfReport_CompletedOnAMissedSlot_MakesItDone_AndTheTaskInProgress()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var slot = await NewSlotAsync(factory, s, task.Id, startIn: -120, length: 30);
            await RunJobAsync(factory, jobs => jobs.SlotDue(slot.Id));
            await RunJobAsync(factory, jobs => jobs.SlotEnded(slot.Id));
            Assert.Equal("Missed", await SlotStatusAsync(s, slot.Id));
            Assert.NotEqual("InProgress", await TaskStatusAsync(s, task.Id));
            var now = factory.Clock.GetUtcNow();

            var response = await s.Client.PostAsJsonAsync("/api/focus/self-report", new
            { taskId = task.Id, scheduleSlotId = slot.Id, startedAt = now.AddMinutes(-100), endedAt = now.AddMinutes(-70), stopReason = "Completed" });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("Done", await SlotStatusAsync(s, slot.Id));
            Assert.Equal("InProgress", await TaskStatusAsync(s, task.Id));
            var item = Assert.Single(await SessionsAsync(s, task.Id));
            Assert.Equal(METANOIA.Domain.Enums.CaptureMethod.SelfReported, item.CaptureMethod);
            Assert.Equal("Stopped", item.State);
        }

        [Fact]
        public async Task MissedSlot_TakesTheTaskBackFromScheduledToToDo()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var slot = await NewSlotAsync(factory, s, task.Id, length: 30);
            Assert.Equal("Scheduled", await TaskStatusAsync(s, task.Id));

            await RunJobAsync(factory, jobs => jobs.SlotDue(slot.Id));
            Assert.Equal("Scheduled", await TaskStatusAsync(s, task.Id));        // Due vẫn là slot sống
            await RunJobAsync(factory, jobs => jobs.SlotEnded(slot.Id));

            Assert.Equal("Missed", await SlotStatusAsync(s, slot.Id));
            Assert.Equal("ToDo", await TaskStatusAsync(s, task.Id));
        }

        // ---------------------------------------------------------------- API danh sách phiên

        [Fact]
        public async Task TaskSessions_ListEveryStateOfASession_AndOnlyForTheOwner()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var (other, _) = await TestHelpers.LoginAsync(factory, "user-2");
            var task = await NewTaskAsync(s);

            var stopped = await StartOkAsync(s, task.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(5));
            await StopOkAsync(s, stopped, "Interrupted");
            var running = await StartOkAsync(s, task.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(3));

            var list = await SessionsAsync(s, task.Id);

            Assert.Equal(new[] { "Stopped", "Running" }, list.Select(x => x.State).ToArray());
            Assert.Equal(5, list[0].Minutes);
            Assert.Equal(3, list[1].Minutes);
            Assert.Null(list[1].EndedAt);
            Assert.Equal(running, list[1].SessionId);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/focus/task/{task.Id}/sessions")).StatusCode);
        }

        // ---------------------------------------------------------------- Milestone

        [Fact]
        public async Task Milestone_FollowsItsLifecycle_WithTimestampsAndRevisionCount()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var m = await NewMilestoneAsync(s);

            Assert.Equal(HttpStatusCode.BadRequest, (await PutMilestoneAsync(s, m.Id, 1, "Approved")).StatusCode);   // Planned không nhảy cóc
            Assert.Equal(HttpStatusCode.OK, (await PutMilestoneAsync(s, m.Id, 1, "InProgress")).StatusCode);

            var delivered = await (await PutMilestoneAsync(s, m.Id, 1, "Delivered")).Content.ReadFromJsonAsync<MilestoneDto>();
            Assert.NotNull(delivered!.DeliveredAt);

            // Khách yêu cầu sửa: InRevision tăng RevisionCount
            var revision = await (await PutMilestoneAsync(s, m.Id, 1, "InRevision")).Content.ReadFromJsonAsync<MilestoneDto>();
            Assert.Equal(1, revision!.RevisionCount);
            Assert.Equal(HttpStatusCode.BadRequest, (await PutMilestoneAsync(s, m.Id, 1, "Approved")).StatusCode);   // phải giao lại trước

            await PutMilestoneAsync(s, m.Id, 1, "Delivered");
            var approved = await (await PutMilestoneAsync(s, m.Id, 1, "Approved")).Content.ReadFromJsonAsync<MilestoneDto>();
            Assert.NotNull(approved!.ApprovedAt);

            // Khách đổi ý sau khi đã duyệt: mở lại, xóa ApprovedAt, đếm thêm một lần chỉnh sửa
            var reopened = await (await PutMilestoneAsync(s, m.Id, 1, "InRevision")).Content.ReadFromJsonAsync<MilestoneDto>();
            Assert.Equal(2, reopened!.RevisionCount);
            Assert.Null(reopened.ApprovedAt);
        }

        [Fact]
        public async Task Milestone_PutWithoutStatus_KeepsStatus_AndRevisionCountNeverDrops()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var m = await NewMilestoneAsync(s);
            await PutMilestoneAsync(s, m.Id, 1, "InProgress");
            await PutMilestoneAsync(s, m.Id, 1, "Delivered");
            await PutMilestoneAsync(s, m.Id, 1, "InRevision");   // RevisionCount = 1

            var updated = await (await PutMilestoneAsync(s, m.Id, 1, null)).Content.ReadFromJsonAsync<MilestoneDto>();

            Assert.Equal("InRevision", updated!.Status);
            Assert.Equal(1, updated.RevisionCount);              // request gửi 0 nhưng không làm giảm
        }

        [Fact]
        public async Task Milestone_CanOnlyBeDelivered_WhenEveryTaskIsDoneOrSkipped()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var m = await NewMilestoneAsync(s);
            var t1 = await NewTaskAsync(s, "Việc 1", m.Id);
            var t2 = await NewTaskAsync(s, "Việc 2", m.Id);
            await PutMilestoneAsync(s, m.Id, 1, "InProgress");

            var blocked = await PutMilestoneAsync(s, m.Id, 1, "Delivered");
            Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
            Assert.Contains("2 task", await blocked.Content.ReadAsStringAsync());

            await PutTaskAsync(s, t1.Id, "Done");
            Assert.Equal(HttpStatusCode.BadRequest, (await PutMilestoneAsync(s, m.Id, 1, "Delivered")).StatusCode);

            await PutTaskAsync(s, t2.Id, "Skipped");
            Assert.Equal(HttpStatusCode.OK, (await PutMilestoneAsync(s, m.Id, 1, "Delivered")).StatusCode);
        }

        [Fact]
        public async Task Milestone_MovesFromPlannedToInProgress_WhenAFirstTaskStartsWorking()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var m = await NewMilestoneAsync(s);
            var task = await NewTaskAsync(s, "Việc", m.Id);
            Assert.Equal("Planned", (await s.Client.GetFromJsonAsync<MilestoneDto>($"/api/milestones/{m.Id}"))!.Status);

            await StartOkAsync(s, task.Id);

            Assert.Equal("InProgress", (await s.Client.GetFromJsonAsync<MilestoneDto>($"/api/milestones/{m.Id}"))!.Status);
        }

        [Fact]
        public async Task DeliveredMilestone_DoesNotTakeNewOrReopenedTasks_UntilItIsInRevision()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var m = await NewMilestoneAsync(s);
            var done = await NewTaskAsync(s, "Đã xong", m.Id);
            var elsewhere = await NewTaskAsync(s, "Ở nơi khác");
            await PutTaskAsync(s, done.Id, "Done");
            await PutMilestoneAsync(s, m.Id, 1, "InProgress");
            await PutMilestoneAsync(s, m.Id, 1, "Delivered");

            // Task mới, chuyển task vào, hay mở lại task trong giai đoạn đã giao đều bị chặn
            var create = await s.Client.PostAsJsonAsync($"/api/projects/{s.ProjectId}/tasks", new TaskRequestDto
            {
                Title = "Thêm việc", TaskType = "Flexible", DomainId = s.DomainId, MilestoneId = m.Id,
                Deadline = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), EstimatedMinutes = 30
            });
            Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await PutTaskAsync(s, elsewhere.Id, null, m.Id, changeMilestone: true)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await PutTaskAsync(s, done.Id, "ToDo")).StatusCode);

            // Chuyển InRevision thì nhận lại được
            await PutMilestoneAsync(s, m.Id, 1, "InRevision");
            Assert.Equal(HttpStatusCode.OK, (await PutTaskAsync(s, done.Id, "ToDo")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PutTaskAsync(s, elsewhere.Id, null, m.Id, changeMilestone: true)).StatusCode);
        }

        // ---------------------------------------------------------------- Đề xuất AI

        [Fact]
        public async Task ApprovingAnAiProposal_MakesTheTasksOfItsSlotsScheduled()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var task = await NewTaskAsync(s);
            var proposalId = Guid.NewGuid();
            var slotId = Guid.NewGuid();
            await factory.SeedAsync(db =>
            {
                db.ScheduleProposals.Add(new DomainEntities.ScheduleProposal
                { Id = proposalId, UserId = s.UserId, Level = 3, Status = "Pending", IsFeasible = true, Explanation = "Đề xuất thử", CreatedAt = DateTime.UtcNow });
                db.ScheduleSlots.Add(new DomainEntities.ScheduleSlot
                {
                    Id = slotId, TaskId = task.Id, ScheduleProposalId = proposalId,
                    StartTime = new DateTime(2030, 1, 1, 9, 0, 0, DateTimeKind.Utc), EndTime = new DateTime(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                    ProposedMinutes = 60, Status = "Proposed"
                });
            });
            Assert.Equal("ToDo", await TaskStatusAsync(s, task.Id));

            var approve = await s.Client.PostAsync($"/api/schedule/proposals/{proposalId}/approve", null);

            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
            Assert.Equal("Scheduled", await TaskStatusAsync(s, task.Id));
        }
    }
}
