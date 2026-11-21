using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using METANOIA.Application.Dto;
using METANOIA.Domain.Entities;
using METANOIA.Test.Infrastructure;
using Xunit;
using DomainEntities = METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // Nhánh Group đợt 2: bấm giờ làm việc, kiểm tra lịch người nhận việc, đề xuất đổi giờ (docs/GROUP_FEATURE.md mục 11).
    public class GroupPhase2Tests
    {
        private static readonly DateTimeOffset Nine = new(2030, 3, 1, 9, 0, 0, TimeSpan.Zero);

        private sealed record Actor(HttpClient Client, Guid UserId, string Email);

        private static async Task<Actor> LoginAsync(ApiFactory factory, string subject)
        {
            var (client, auth) = await TestHelpers.LoginAsync(factory, subject);
            return new Actor(client, auth.UserId, auth.Email);
        }

        private static async Task<GroupDto> CreateGroupAsync(Actor leader, string name = "Nhóm đồ án")
        {
            var response = await leader.Client.PostAsJsonAsync("/api/groups", new GroupRequestDto { Name = name });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<GroupDto>())!;
        }

        private static async Task JoinAsync(Actor leader, Actor invitee, Guid groupId, string role)
        {
            var invite = await leader.Client.PostAsJsonAsync($"/api/groups/{groupId}/invitations",
                new GroupInvitationRequestDto { Email = invitee.Email, Role = role });
            Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
            var mine = await invitee.Client.GetFromJsonAsync<List<GroupInvitationDto>>("/api/group-invitations");
            var invitation = Assert.Single(mine!, i => i.GroupId == groupId);
            Assert.Equal(HttpStatusCode.OK, (await invitee.Client.PostAsync($"/api/group-invitations/{invitation.Id}/accept", null)).StatusCode);
        }

        private static async Task<GroupTaskDto> CreateTaskAsync(Actor actor, Guid groupId, GroupTaskRequestDto request)
        {
            var response = await actor.Client.PostAsJsonAsync($"/api/groups/{groupId}/tasks", request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<GroupTaskDto>())!;
        }

        private static Task<GroupTaskDto> CreateTaskAsync(Actor actor, Guid groupId, string title, Guid? assigneeId = null) =>
            CreateTaskAsync(actor, groupId, new GroupTaskRequestDto { Title = title, AssigneeId = assigneeId });

        private static async Task<GroupSubTaskDto> CreateSubTaskAsync(Actor actor, Guid taskId, string title, Guid? assigneeId = null)
        {
            var response = await actor.Client.PostAsJsonAsync($"/api/group-tasks/{taskId}/subtasks",
                new GroupSubTaskRequestDto { Title = title, AssigneeId = assigneeId });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<GroupSubTaskDto>())!;
        }

        private static async Task<GroupTaskDto> GetTaskAsync(Actor actor, Guid taskId) =>
            (await actor.Client.GetFromJsonAsync<GroupTaskDto>($"/api/group-tasks/{taskId}"))!;

        private static async Task<GroupSubTaskDto> GetSubTaskAsync(Actor actor, Guid subTaskId) =>
            (await actor.Client.GetFromJsonAsync<GroupSubTaskDto>($"/api/group-subtasks/{subTaskId}"))!;

        private static Task<HttpResponseMessage> StartAsync(Actor actor, Guid taskId, Guid? subTaskId = null) =>
            actor.Client.PostAsJsonAsync($"/api/group-tasks/{taskId}/focus/start", new GroupFocusStartRequestDto { SubTaskId = subTaskId });

        private static Task<HttpResponseMessage> StopAsync(Actor actor, Guid sessionId, string reason, string? note = null) =>
            actor.Client.PostAsJsonAsync($"/api/group-focus/{sessionId}/stop", new { stopReason = reason, note });

        private static async Task<GroupFocusStartResponseDto> StartOkAsync(Actor actor, Guid taskId, Guid? subTaskId = null)
        {
            var response = await StartAsync(actor, taskId, subTaskId);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<GroupFocusStartResponseDto>())!;
        }

        private static async Task<GroupFocusStopResponseDto> StopOkAsync(Actor actor, Guid sessionId, string reason, string? note = null)
        {
            var response = await StopAsync(actor, sessionId, reason, note);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<GroupFocusStopResponseDto>())!;
        }

        private static async Task<string> MessageOfAsync(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("message").GetString() ?? string.Empty;
        }

        private static async Task<string> CodeOfAsync(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() ?? string.Empty : string.Empty;
        }

        // ================================================================ Bấm giờ

        [Fact]
        public async Task Focus_StartAndStopCompleted_DrivesSubTaskAndTaskStatus_AndTracksMinutes()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            var milestone = await (await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/milestones",
                new GroupMilestoneRequestDto { Name = "M1", OrderIndex = 1 })).Content.ReadFromJsonAsync<GroupMilestoneDto>();
            var task = await CreateTaskAsync(leader, group.Id, new GroupTaskRequestDto { Title = "Việc", MilestoneId = milestone!.Id, EstimatedMinutes = 120 });
            var s1 = await CreateSubTaskAsync(leader, task.Id, "Phần 1", a.UserId);
            var s2 = await CreateSubTaskAsync(leader, task.Id, "Phần 2", a.UserId);

            var started = await StartOkAsync(a, task.Id, s1.Id);
            Assert.Equal("InProgress", started.SubTaskStatus);
            Assert.Equal("InProgress", started.TaskStatus);
            Assert.Equal("InProgress", (await leader.Client.GetFromJsonAsync<GroupMilestoneDto>($"/api/group-milestones/{milestone.Id}"))!.Status);

            var current = (await a.Client.GetFromJsonAsync<GroupFocusCurrentDto>("/api/group-focus/current"))!;
            Assert.Equal(started.SessionId, current.SessionId);
            Assert.Equal("Phần 1", current.SubTaskTitle);
            Assert.Equal("Nhóm đồ án", current.GroupName);
            Assert.Equal(120, current.EstimatedMinutes);

            factory.Clock.Advance(TimeSpan.FromMinutes(25));
            var stopped = await StopOkAsync(a, started.SessionId, "Completed");
            Assert.Equal(25, stopped.SessionMinutes);
            Assert.Equal(25, stopped.MyTaskMinutes);
            Assert.Equal("Done", stopped.SubTaskStatus);
            Assert.Equal("InProgress", stopped.TaskStatus);
            Assert.Equal(HttpStatusCode.NoContent, (await a.Client.GetAsync("/api/group-focus/current")).StatusCode);

            // Xong việc con cuối: việc cha tự Done
            var second = await StartOkAsync(a, task.Id, s2.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(10));
            var done = await StopOkAsync(a, second.SessionId, "Completed");
            Assert.Equal("Done", done.TaskStatus);
            Assert.Equal(35, done.TaskTotalMinutes);
            Assert.Equal("Done", (await GetTaskAsync(leader, task.Id)).Status);
            Assert.Equal("Done", (await GetSubTaskAsync(leader, s2.Id)).Status);
        }

        [Fact]
        public async Task Focus_TaskWithoutSubTasks_CompletedMakesTaskDone_InterruptedKeepsItInProgress()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            var task = await CreateTaskAsync(leader, group.Id, "Một mình", a.UserId);

            var first = await StartOkAsync(a, task.Id);
            Assert.Equal("InProgress", first.TaskStatus);
            factory.Clock.Advance(TimeSpan.FromMinutes(15));
            var interrupted = await StopOkAsync(a, first.SessionId, "Interrupted");
            Assert.Equal("InProgress", interrupted.TaskStatus);
            Assert.Equal("InProgress", (await GetTaskAsync(leader, task.Id)).Status);

            var second = await StartOkAsync(a, task.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(20));
            var done = await StopOkAsync(a, second.SessionId, "Completed");
            Assert.Equal("Done", done.TaskStatus);
            Assert.Equal(35, done.MyTaskMinutes);
            Assert.NotNull((await GetTaskAsync(leader, task.Id)).CompletedAt);

            // Việc đã Done không bấm giờ tiếp được
            Assert.Equal(HttpStatusCode.BadRequest, (await StartAsync(a, task.Id)).StatusCode);
        }

        [Fact]
        public async Task Focus_StartRules_OnlyAssigneeOfAnOpenSubTask_AndSubTaskRequiredWhenTaskHasSome()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var b = await LoginAsync(factory, "b");
            var viewer = await LoginAsync(factory, "viewer");
            var outsider = await LoginAsync(factory, "outsider");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            await JoinAsync(leader, b, group.Id, "Member");
            await JoinAsync(leader, viewer, group.Id, "Viewer");
            var task = await CreateTaskAsync(leader, group.Id, "Việc", a.UserId);
            var mine = await CreateSubTaskAsync(leader, task.Id, "Của A", a.UserId);
            var theirs = await CreateSubTaskAsync(leader, task.Id, "Của B", b.UserId);
            var unassigned = await CreateSubTaskAsync(leader, task.Id, "Chưa giao");

            // Việc có việc con: phải chọn việc con
            Assert.Equal(HttpStatusCode.BadRequest, (await StartAsync(a, task.Id)).StatusCode);
            // Việc con của người khác
            Assert.Equal(HttpStatusCode.Forbidden, (await StartAsync(a, task.Id, theirs.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await StartAsync(viewer, task.Id, mine.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await StartAsync(outsider, task.Id, mine.Id)).StatusCode);
            // Việc con không thuộc việc này
            var other = await CreateTaskAsync(leader, group.Id, "Việc khác");
            var foreign = await CreateSubTaskAsync(leader, other.Id, "Của việc khác", a.UserId);
            Assert.Equal(HttpStatusCode.BadRequest, (await StartAsync(a, task.Id, foreign.Id)).StatusCode);

            // Việc con chưa giao: người được giao việc cha bấm được
            Assert.Equal(HttpStatusCode.Created, (await StartAsync(a, task.Id, unassigned.Id)).StatusCode);

            // Người được giao việc con bấm được kể cả không được giao việc cha
            Assert.Equal(HttpStatusCode.Created, (await StartAsync(b, task.Id, theirs.Id)).StatusCode);
        }

        [Fact]
        public async Task Focus_OneRunningSessionPerPerson_GroupAndPersonal()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            var t1 = await CreateTaskAsync(leader, group.Id, "Một", a.UserId);
            var t2 = await CreateTaskAsync(leader, group.Id, "Hai", a.UserId);

            var running = await StartOkAsync(a, t1.Id);
            var conflict = await StartAsync(a, t2.Id);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("FOCUS_SESSION_CONFLICT", await CodeOfAsync(conflict));

            await StopOkAsync(a, running.SessionId, "Interrupted");

            // Phiên cá nhân đang chạy cũng chặn bấm giờ việc nhóm (đọc IFocusSessionRepository, không sửa nhánh cá nhân)
            var domainId = Guid.NewGuid();
            await factory.SeedAsync(db => db.Domains.Add(new DomainEntities.Domain { Id = domainId, Name = $"Dev-{Guid.NewGuid()}", IsActive = true }));
            var project = await (await a.Client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Cá nhân" })).Content.ReadFromJsonAsync<ProjectDto>();
            var personalTask = await (await a.Client.PostAsJsonAsync($"/api/projects/{project!.Id}/tasks", new TaskRequestDto
            {
                Title = "Việc cá nhân",
                TaskType = "Flexible",
                DomainId = domainId,
                Deadline = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                EstimatedMinutes = 60
            })).Content.ReadFromJsonAsync<TaskDto>();
            var personal = await a.Client.PostAsJsonAsync("/api/focus/start", new { taskId = personalTask!.Id });
            Assert.Equal(HttpStatusCode.Created, personal.StatusCode);

            var blocked = await StartAsync(a, t2.Id);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Contains("cá nhân", await MessageOfAsync(blocked));
        }

        [Fact]
        public async Task Focus_StopRules_NoteForBlockedOrOther_OnlyOwner_NotTwice()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var b = await LoginAsync(factory, "b");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            await JoinAsync(leader, b, group.Id, "Member");
            var task = await CreateTaskAsync(leader, group.Id, "Việc", a.UserId);
            var started = await StartOkAsync(a, task.Id);

            Assert.Equal(HttpStatusCode.BadRequest, (await StopAsync(a, started.SessionId, "Blocked")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await StopAsync(a, started.SessionId, "Other", "  ")).StatusCode);
            // Phiên của người khác coi như không tồn tại
            Assert.Equal(HttpStatusCode.NotFound, (await StopAsync(b, started.SessionId, "Interrupted")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await StopAsync(leader, started.SessionId, "Interrupted")).StatusCode);

            var stopped = await StopAsync(a, started.SessionId, "Blocked", "Chờ khách phản hồi");
            Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
            var again = await StopAsync(a, started.SessionId, "Interrupted");
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        }

        [Fact]
        public async Task Focus_ForgottenSession_IsCappedAtTwelveHours()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            var task = await CreateTaskAsync(leader, group.Id, "Việc", a.UserId);
            var started = await StartOkAsync(a, task.Id);

            factory.Clock.Advance(TimeSpan.FromHours(20));
            var stopped = await StopOkAsync(a, started.SessionId, "Interrupted");

            Assert.True(stopped.WasCapped);
            Assert.Equal(720, stopped.SessionMinutes);
        }

        [Fact]
        public async Task Focus_TimeReport_PerPersonPerSubTask_AndInGroupProgress()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var b = await LoginAsync(factory, "b");
            var viewer = await LoginAsync(factory, "viewer");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            await JoinAsync(leader, b, group.Id, "Member");
            await JoinAsync(leader, viewer, group.Id, "Viewer");
            var task = await CreateTaskAsync(leader, group.Id, new GroupTaskRequestDto { Title = "Báo cáo", EstimatedMinutes = 300 });
            var sa = await CreateSubTaskAsync(leader, task.Id, "Phần A", a.UserId);
            var sb = await CreateSubTaskAsync(leader, task.Id, "Phần B", b.UserId);

            var s1 = await StartOkAsync(a, task.Id, sa.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(30));
            await StopOkAsync(a, s1.SessionId, "Interrupted");

            var s2 = await StartOkAsync(b, task.Id, sb.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(45));
            await StopOkAsync(b, s2.SessionId, "Interrupted");

            var s3 = await StartOkAsync(a, task.Id, sa.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(15));
            await StopOkAsync(a, s3.SessionId, "Interrupted");

            // Mọi thành viên (kể cả Viewer) xem được báo cáo giờ
            var time = (await viewer.Client.GetFromJsonAsync<GroupTaskTimeDto>($"/api/group-tasks/{task.Id}/time"))!;
            Assert.Equal(90, time.TotalMinutes);
            Assert.Equal(300, time.EstimatedMinutes);
            Assert.Equal(3, time.SessionCount);
            Assert.Equal(45, time.ByUser.Single(u => u.UserId == a.UserId).Minutes);
            Assert.Equal(2, time.ByUser.Single(u => u.UserId == a.UserId).SessionCount);
            Assert.Equal(45, time.ByUser.Single(u => u.UserId == b.UserId).Minutes);
            Assert.Equal(45, time.BySubTask.Single(x => x.SubTaskId == sa.Id).Minutes);
            Assert.Equal("Phần B", time.BySubTask.Single(x => x.SubTaskId == sb.Id).Title);

            var progress = (await leader.Client.GetFromJsonAsync<GroupProgressDto>($"/api/groups/{group.Id}/progress"))!;
            Assert.Equal(90, progress.TotalTrackedMinutes);
            Assert.Equal(300, progress.TotalEstimatedMinutes);
            Assert.Equal(45, progress.Members.Single(m => m.UserId == a.UserId).TrackedMinutes);
            Assert.Equal(0, progress.Members.Single(m => m.UserId == leader.UserId).TrackedMinutes);

            // Người ngoài nhóm không xem được
            var outsider = await LoginAsync(factory, "outsider");
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.GetAsync($"/api/group-tasks/{task.Id}/time")).StatusCode);
        }

        [Fact]
        public async Task Focus_RemovingAMember_StopsTheirRunningSession_AndFreesThemToWorkElsewhere()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var g1 = await CreateGroupAsync(leader, "Nhóm 1");
            var g2 = await CreateGroupAsync(leader, "Nhóm 2");
            await JoinAsync(leader, a, g1.Id, "Member");
            await JoinAsync(leader, a, g2.Id, "Member");
            var t1 = await CreateTaskAsync(leader, g1.Id, "Việc nhóm 1", a.UserId);
            var t2 = await CreateTaskAsync(leader, g2.Id, "Việc nhóm 2", a.UserId);

            var running = await StartOkAsync(a, t1.Id);
            factory.Clock.Advance(TimeSpan.FromMinutes(5));
            Assert.Equal(HttpStatusCode.NoContent, (await leader.Client.DeleteAsync($"/api/groups/{g1.Id}/members/{a.UserId}")).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await a.Client.GetAsync("/api/group-focus/current")).StatusCode);
            var time = (await leader.Client.GetFromJsonAsync<GroupTaskTimeDto>($"/api/group-tasks/{t1.Id}/time"))!;
            Assert.False(time.Sessions.Single().IsRunning);
            Assert.Equal(HttpStatusCode.Created, (await StartAsync(a, t2.Id)).StatusCode);
            _ = running;
        }

        [Fact]
        public async Task Focus_ArchivedGroup_CannotStart_ButRunningSessionCanStillBeStopped()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            var task = await CreateTaskAsync(leader, group.Id, "Việc", a.UserId);
            var started = await StartOkAsync(a, task.Id);

            await leader.Client.PostAsync($"/api/groups/{group.Id}/archive", null);
            factory.Clock.Advance(TimeSpan.FromMinutes(10));

            var stopped = await StopOkAsync(a, started.SessionId, "Completed");
            Assert.Equal(10, stopped.SessionMinutes);
            // Nhóm đã lưu trữ: giờ được ghi nhưng status không đổi
            Assert.Equal("InProgress", (await GetTaskAsync(leader, task.Id)).Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await StartAsync(a, task.Id)).StatusCode);
        }

        // ================================================================ Kiểm tra lịch khi giao việc

        [Fact]
        public async Task Assign_FixedTimeOverlappingAnotherGroupTask_Returns409_UntilConfirmed()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var g1 = await CreateGroupAsync(leader, "Nhóm 1");
            var g2 = await CreateGroupAsync(leader, "Nhóm 2");
            await JoinAsync(leader, a, g1.Id, "Member");
            await JoinAsync(leader, a, g2.Id, "Member");
            await CreateTaskAsync(leader, g1.Id, new GroupTaskRequestDto { Title = "Họp 1", AssigneeId = a.UserId, FixedStart = Nine, FixedEnd = Nine.AddHours(1) });

            // Khác nhóm cũng trùng: lịch là của người, không phải của nhóm
            var overlap = new GroupTaskRequestDto { Title = "Họp 2", AssigneeId = a.UserId, FixedStart = Nine.AddMinutes(30), FixedEnd = Nine.AddMinutes(90) };
            var blocked = await leader.Client.PostAsJsonAsync($"/api/groups/{g2.Id}/tasks", overlap);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            var body = await blocked.Content.ReadAsStringAsync();
            Assert.Contains("GROUP_ASSIGNEE_BUSY", body);
            Assert.Contains("GroupTask", body);
            Assert.DoesNotContain("Họp 1", body);

            // Chạm đầu mút không tính là trùng
            var touching = await leader.Client.PostAsJsonAsync($"/api/groups/{g2.Id}/tasks",
                new GroupTaskRequestDto { Title = "Họp 3", AssigneeId = a.UserId, FixedStart = Nine.AddHours(1), FixedEnd = Nine.AddHours(2) });
            Assert.Equal(HttpStatusCode.Created, touching.StatusCode);

            overlap.ConfirmConflicts = true;
            var confirmed = await leader.Client.PostAsJsonAsync($"/api/groups/{g2.Id}/tasks", overlap);
            Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        }

        [Fact]
        public async Task Assign_ChecksPersonalScheduleAndGoogle_WithoutLeakingTitles()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");

            // Slot cá nhân đang hoạt động của A lúc 9h
            var domainId = Guid.NewGuid();
            await factory.SeedAsync(db => db.Domains.Add(new DomainEntities.Domain { Id = domainId, Name = $"Dev-{Guid.NewGuid()}", IsActive = true }));
            var project = await (await a.Client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Bí mật" })).Content.ReadFromJsonAsync<ProjectDto>();
            var personalTask = await (await a.Client.PostAsJsonAsync($"/api/projects/{project!.Id}/tasks", new TaskRequestDto
            {
                Title = "Việc riêng tư",
                TaskType = "Flexible",
                DomainId = domainId,
                Deadline = new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero),
                EstimatedMinutes = 60
            })).Content.ReadFromJsonAsync<TaskDto>();
            var slot = await a.Client.PostAsJsonAsync($"/api/tasks/{personalTask!.Id}/schedule-slots", new ScheduleSlotRequestDto
            {
                StartTime = Nine,
                EndTime = Nine.AddHours(1),
                IsManualOverride = true,
                Status = "Approved"
            });
            Assert.Equal(HttpStatusCode.Created, slot.StatusCode);

            // Event Google lúc 14h
            var connectionId = Guid.NewGuid();
            await factory.SeedAsync(db =>
            {
                db.GoogleConnections.Add(new GoogleConnection
                {
                    Id = connectionId,
                    UserId = a.UserId,
                    AccessTokenEncrypted = new byte[] { 1 },
                    RefreshTokenEncrypted = new byte[] { 1 },
                    TokenExpiresAt = DateTime.UtcNow.AddHours(1),
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow
                });
                db.GoogleCalendarEvents.Add(new GoogleCalendarEvent
                {
                    Id = Guid.NewGuid(),
                    GoogleConnectionId = connectionId,
                    GoogleEventId = "e1",
                    Title = "Khám bệnh",
                    StartTime = Nine.AddHours(5).UtcDateTime,
                    EndTime = Nine.AddHours(6).UtcDateTime,
                    Etag = "etag",
                    UpdatedAt = DateTime.UtcNow
                });
            });

            var meeting = await CreateTaskAsync(leader, group.Id, new GroupTaskRequestDto { Title = "Họp nhóm", FixedStart = Nine.AddMinutes(30), FixedEnd = Nine.AddMinutes(90) });
            var check = await leader.Client.GetAsync($"/api/group-tasks/{meeting.Id}/assignee-check?assigneeId={a.UserId}");
            var raw = await check.Content.ReadAsStringAsync();
            var dto = JsonSerializer.Deserialize<GroupAssigneeAvailabilityDto>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.True(dto.IsBusy);
            Assert.True(dto.NeedsConfirmation);
            Assert.Equal("PersonalSchedule", Assert.Single(dto.Conflicts).Source);
            // Chỉ bận hoặc rảnh: không lộ tên dự án hay tiêu đề lịch cá nhân
            Assert.DoesNotContain("Bí mật", raw);
            Assert.DoesNotContain("Việc riêng tư", raw);

            var assign = await leader.Client.PutAsJsonAsync($"/api/group-tasks/{meeting.Id}/assignee", new GroupAssigneeRequestDto { AssigneeId = a.UserId });
            Assert.Equal(HttpStatusCode.Conflict, assign.StatusCode);
            Assert.Equal("GROUP_ASSIGNEE_BUSY", await CodeOfAsync(assign));

            // Giờ trùng event Google
            var visit = await CreateTaskAsync(leader, group.Id, new GroupTaskRequestDto { Title = "Họp chiều", FixedStart = Nine.AddHours(5).AddMinutes(15), FixedEnd = Nine.AddHours(5).AddMinutes(45) });
            var googleCheck = (await leader.Client.GetFromJsonAsync<GroupAssigneeAvailabilityDto>($"/api/group-tasks/{visit.Id}/assignee-check?assigneeId={a.UserId}"))!;
            Assert.Equal("GoogleCalendar", Assert.Single(googleCheck.Conflicts).Source);

            // Khung giờ trống thì giao được không cần xác nhận
            var free = await CreateTaskAsync(leader, group.Id, new GroupTaskRequestDto { Title = "Họp tối", FixedStart = Nine.AddHours(10), FixedEnd = Nine.AddHours(11) });
            var assignFree = await leader.Client.PutAsJsonAsync($"/api/group-tasks/{free.Id}/assignee", new GroupAssigneeRequestDto { AssigneeId = a.UserId });
            Assert.Equal(HttpStatusCode.OK, assignFree.StatusCode);

            var confirmed = await leader.Client.PutAsJsonAsync($"/api/group-tasks/{meeting.Id}/assignee", new GroupAssigneeRequestDto { AssigneeId = a.UserId, ConfirmConflicts = true });
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        }

        [Fact]
        public async Task Assign_WorkloadOverCapacity_Warns_AndOnlyLeaderCanPreview()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var b = await LoginAsync(factory, "b");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            await JoinAsync(leader, b, group.Id, "Member");
            var deadline = new DateTimeOffset(2030, 5, 10, 17, 0, 0, TimeSpan.Zero);

            // Giờ làm mặc định 8h đến 18h, 5 ngày: 3000 phút một tuần
            await CreateTaskAsync(leader, group.Id, new GroupTaskRequestDto { Title = "Nặng 1", AssigneeId = a.UserId, Deadline = deadline, EstimatedMinutes = 2000 });
            var heavy = new GroupTaskRequestDto { Title = "Nặng 2", AssigneeId = a.UserId, Deadline = deadline, EstimatedMinutes = 1500 };

            var blocked = await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/tasks", heavy);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Contains("3500", await blocked.Content.ReadAsStringAsync());

            // Người khác còn rảnh thì giao được; việc nhỏ cũng giao được
            heavy.AssigneeId = b.UserId;
            Assert.Equal(HttpStatusCode.Created, (await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/tasks", heavy)).StatusCode);
            var small = await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/tasks",
                new GroupTaskRequestDto { Title = "Nhỏ", AssigneeId = a.UserId, Deadline = deadline, EstimatedMinutes = 500 });
            Assert.Equal(HttpStatusCode.Created, small.StatusCode);

            heavy.AssigneeId = a.UserId;
            heavy.ConfirmConflicts = true;
            Assert.Equal(HttpStatusCode.Created, (await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/tasks", heavy)).StatusCode);

            // Member không xem trước được lịch của người khác
            var task = await CreateTaskAsync(leader, group.Id, "Xem trước");
            Assert.Equal(HttpStatusCode.Forbidden, (await a.Client.GetAsync($"/api/group-tasks/{task.Id}/assignee-check?assigneeId={b.UserId}")).StatusCode);
            var outsider = await LoginAsync(factory, "outsider");
            Assert.Equal(HttpStatusCode.BadRequest, (await leader.Client.GetAsync($"/api/group-tasks/{task.Id}/assignee-check?assigneeId={outsider.UserId}")).StatusCode);
        }

        [Fact]
        public async Task UpdateTask_ChangingTimeOfAnAssignedTask_ChecksTheAssigneeAgain()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            await CreateTaskAsync(leader, group.Id, new GroupTaskRequestDto { Title = "Cố định", AssigneeId = a.UserId, FixedStart = Nine, FixedEnd = Nine.AddHours(1) });
            var other = await CreateTaskAsync(leader, group.Id, new GroupTaskRequestDto { Title = "Việc khác", AssigneeId = a.UserId, FixedStart = Nine.AddHours(3), FixedEnd = Nine.AddHours(4) });

            var move = new GroupTaskRequestDto { Title = "Việc khác", FixedStart = Nine.AddMinutes(30), FixedEnd = Nine.AddMinutes(90) };
            var blocked = await leader.Client.PutAsJsonAsync($"/api/group-tasks/{other.Id}", move);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

            // Đổi tiêu đề mà không đổi giờ thì không kiểm tra lại
            var rename = await leader.Client.PutAsJsonAsync($"/api/group-tasks/{other.Id}",
                new GroupTaskRequestDto { Title = "Đổi tên", FixedStart = other.FixedStart, FixedEnd = other.FixedEnd });
            Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

            move.ConfirmConflicts = true;
            var confirmed = await leader.Client.PutAsJsonAsync($"/api/group-tasks/{other.Id}", move);
            var dto = (await confirmed.Content.ReadFromJsonAsync<GroupTaskDto>())!;
            Assert.Equal(Nine.AddMinutes(30), dto.FixedStart);
        }

        [Fact]
        public async Task FixedTime_MustHaveBothEnds_AndEndAfterStart()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var group = await CreateGroupAsync(leader);

            var onlyStart = await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/tasks", new GroupTaskRequestDto { Title = "x", FixedStart = Nine });
            var reversed = await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/tasks", new GroupTaskRequestDto { Title = "x", FixedStart = Nine, FixedEnd = Nine.AddHours(-1) });
            var ok = await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/tasks", new GroupTaskRequestDto { Title = "x", FixedStart = Nine, FixedEnd = Nine.AddHours(1) });

            Assert.Equal(HttpStatusCode.BadRequest, onlyStart.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
            var dto = (await ok.Content.ReadFromJsonAsync<GroupTaskDto>())!;
            Assert.Equal(Nine, dto.FixedStart);
            Assert.Equal(Nine.AddHours(1), dto.FixedEnd);
        }

        // ================================================================ Đề xuất đổi giờ

        [Fact]
        public async Task ChangeRequest_AssigneeProposes_LeaderApproves_TaskTimeChanges_AndBothAreNotified()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            var task = await CreateTaskAsync(leader, group.Id, new GroupTaskRequestDto { Title = "Họp khách", AssigneeId = a.UserId, FixedStart = Nine, FixedEnd = Nine.AddHours(1) });

            var created = await a.Client.PostAsJsonAsync($"/api/group-tasks/{task.Id}/change-requests", new GroupChangeRequestCreateDto
            {
                NewFixedStart = Nine.AddHours(2),
                NewFixedEnd = Nine.AddHours(3),
                Reason = "Trùng lịch học"
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var request = (await created.Content.ReadFromJsonAsync<GroupChangeRequestDto>())!;
            Assert.Equal("Pending", request.Status);
            Assert.Equal(Nine, request.CurrentFixedStart);
            Assert.Equal(Nine.AddHours(2), request.NewFixedStart);

            // Leader nhận thông báo kèm liên kết tới việc
            var leaderInbox = (await leader.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!;
            var note = leaderInbox.Single(n => n.Type == "GroupChangeRequested");
            Assert.Equal(group.Id, note.GroupId);
            Assert.Equal(task.Id, note.GroupTaskId);

            var pending = (await leader.Client.GetFromJsonAsync<List<GroupChangeRequestDto>>($"/api/groups/{group.Id}/change-requests?status=Pending"))!;
            Assert.Equal(request.Id, Assert.Single(pending).Id);

            var approved = await leader.Client.PostAsJsonAsync($"/api/group-change-requests/{request.Id}/approve", new GroupChangeDecisionDto { Note = "OK" });
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            var result = (await approved.Content.ReadFromJsonAsync<GroupChangeRequestDto>())!;
            Assert.Equal("Approved", result.Status);
            Assert.Equal(leader.UserId, result.DecidedById);
            Assert.Equal("OK", result.DecisionNote);

            var updated = await GetTaskAsync(leader, task.Id);
            Assert.Equal(Nine.AddHours(2), updated.FixedStart);
            Assert.Equal(Nine.AddHours(3), updated.FixedEnd);

            var memberInbox = (await a.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!;
            var decided = memberInbox.Single(n => n.Type == "GroupChangeDecided");
            Assert.Equal(task.Id, decided.GroupTaskId);
            Assert.Contains("được duyệt", decided.Message);

            // Duyệt lần hai và rút lại sau khi đã duyệt đều bị từ chối
            Assert.Equal(HttpStatusCode.BadRequest, (await leader.Client.PostAsJsonAsync($"/api/group-change-requests/{request.Id}/approve", new GroupChangeDecisionDto())).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await a.Client.PostAsync($"/api/group-change-requests/{request.Id}/cancel", null)).StatusCode);

            var log = (await leader.Client.GetFromJsonAsync<List<GroupActivityDto>>($"/api/groups/{group.Id}/activity"))!;
            Assert.Contains(log, l => l.EntityType == "ChangeRequest" && l.Action == "ChangeApproved");
            Assert.Contains(log, l => l.EntityType == "ChangeRequest" && l.Action == "ChangeRequested");
        }

        [Fact]
        public async Task ChangeRequest_DeadlineOnly_AndValidationRules()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var b = await LoginAsync(factory, "b");
            var viewer = await LoginAsync(factory, "viewer");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            await JoinAsync(leader, b, group.Id, "Member");
            await JoinAsync(leader, viewer, group.Id, "Viewer");
            var deadline = new DateTimeOffset(2030, 6, 1, 17, 0, 0, TimeSpan.Zero);
            var task = await CreateTaskAsync(leader, group.Id, new GroupTaskRequestDto { Title = "Báo cáo", AssigneeId = a.UserId, Deadline = deadline });
            var url = $"/api/group-tasks/{task.Id}/change-requests";

            // Không phải người được giao, Viewer, người ngoài
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Client.PostAsJsonAsync(url, new GroupChangeRequestCreateDto { NewDeadline = deadline.AddDays(1), Reason = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync(url, new GroupChangeRequestCreateDto { NewDeadline = deadline.AddDays(1), Reason = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await (await LoginAsync(factory, "outsider")).Client.PostAsJsonAsync(url, new GroupChangeRequestCreateDto { NewDeadline = deadline.AddDays(1), Reason = "x" })).StatusCode);

            // Dữ liệu sai
            Assert.Equal(HttpStatusCode.BadRequest, (await a.Client.PostAsJsonAsync(url, new GroupChangeRequestCreateDto { Reason = "không có giá trị mới" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await a.Client.PostAsJsonAsync(url, new GroupChangeRequestCreateDto { NewFixedStart = Nine, Reason = "thiếu giờ kết thúc" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await a.Client.PostAsJsonAsync(url, new GroupChangeRequestCreateDto { NewFixedStart = Nine, NewFixedEnd = Nine.AddHours(-1), Reason = "ngược" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await a.Client.PostAsJsonAsync(url, new GroupChangeRequestCreateDto { NewDeadline = deadline.AddDays(1), Reason = " " })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await a.Client.PostAsJsonAsync(url, new GroupChangeRequestCreateDto { NewDeadline = deadline, Reason = "giống hiện tại" })).StatusCode);

            var created = await a.Client.PostAsJsonAsync(url, new GroupChangeRequestCreateDto { NewDeadline = deadline.AddDays(2), Reason = "Cần thêm thời gian" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var request = (await created.Content.ReadFromJsonAsync<GroupChangeRequestDto>())!;

            // Mỗi người một đề xuất đang chờ trên một việc
            Assert.Equal(HttpStatusCode.BadRequest, (await a.Client.PostAsJsonAsync(url, new GroupChangeRequestCreateDto { NewDeadline = deadline.AddDays(3), Reason = "lần nữa" })).StatusCode);

            // Member chỉ duyệt được... không: chỉ Leader duyệt
            Assert.Equal(HttpStatusCode.Forbidden, (await a.Client.PostAsJsonAsync($"/api/group-change-requests/{request.Id}/approve", new GroupChangeDecisionDto())).StatusCode);

            var approved = await leader.Client.PostAsJsonAsync($"/api/group-change-requests/{request.Id}/approve", new GroupChangeDecisionDto());
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            Assert.Equal(deadline.AddDays(2), (await GetTaskAsync(leader, task.Id)).Deadline);
        }

        [Fact]
        public async Task ChangeRequest_Reject_NeedsNote_Cancel_AndVisibility()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var b = await LoginAsync(factory, "b");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            await JoinAsync(leader, b, group.Id, "Member");
            var deadline = new DateTimeOffset(2030, 6, 1, 17, 0, 0, TimeSpan.Zero);
            var taskA = await CreateTaskAsync(leader, group.Id, new GroupTaskRequestDto { Title = "Của A", AssigneeId = a.UserId, Deadline = deadline });
            var taskB = await CreateTaskAsync(leader, group.Id, new GroupTaskRequestDto { Title = "Của B", AssigneeId = b.UserId, Deadline = deadline });

            var ra = (await (await a.Client.PostAsJsonAsync($"/api/group-tasks/{taskA.Id}/change-requests",
                new GroupChangeRequestCreateDto { NewDeadline = deadline.AddDays(1), Reason = "A cần thêm" })).Content.ReadFromJsonAsync<GroupChangeRequestDto>())!;
            var rb = (await (await b.Client.PostAsJsonAsync($"/api/group-tasks/{taskB.Id}/change-requests",
                new GroupChangeRequestCreateDto { NewDeadline = deadline.AddDays(1), Reason = "B cần thêm" })).Content.ReadFromJsonAsync<GroupChangeRequestDto>())!;

            // Member chỉ thấy đề xuất của mình; Leader thấy hết
            Assert.Equal(ra.Id, Assert.Single((await a.Client.GetFromJsonAsync<List<GroupChangeRequestDto>>($"/api/groups/{group.Id}/change-requests"))!).Id);
            Assert.Equal(2, (await leader.Client.GetFromJsonAsync<List<GroupChangeRequestDto>>($"/api/groups/{group.Id}/change-requests"))!.Count);
            Assert.Empty((await a.Client.GetFromJsonAsync<List<GroupChangeRequestDto>>($"/api/group-tasks/{taskB.Id}/change-requests"))!);

            // Từ chối bắt buộc có lý do
            Assert.Equal(HttpStatusCode.BadRequest, (await leader.Client.PostAsJsonAsync($"/api/group-change-requests/{ra.Id}/reject", new GroupChangeDecisionDto { Note = " " })).StatusCode);
            var rejected = await leader.Client.PostAsJsonAsync($"/api/group-change-requests/{ra.Id}/reject", new GroupChangeDecisionDto { Note = "Deadline cứng" });
            var dto = (await rejected.Content.ReadFromJsonAsync<GroupChangeRequestDto>())!;
            Assert.Equal("Rejected", dto.Status);
            Assert.Equal("Deadline cứng", dto.DecisionNote);
            Assert.Equal(deadline, (await GetTaskAsync(leader, taskA.Id)).Deadline);
            var notice = (await a.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!.Single(n => n.Type == "GroupChangeDecided");
            Assert.Contains("bị từ chối", notice.Message);
            Assert.Contains("Deadline cứng", notice.Message);

            // Rút lại: chỉ người đề xuất
            Assert.Equal(HttpStatusCode.Forbidden, (await a.Client.PostAsync($"/api/group-change-requests/{rb.Id}/cancel", null)).StatusCode);
            var cancelled = await b.Client.PostAsync($"/api/group-change-requests/{rb.Id}/cancel", null);
            Assert.Equal("Cancelled", (await cancelled.Content.ReadFromJsonAsync<GroupChangeRequestDto>())!.Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await leader.Client.PostAsJsonAsync($"/api/group-change-requests/{rb.Id}/approve", new GroupChangeDecisionDto())).StatusCode);

            // Đã rút lại thì được gửi đề xuất mới
            Assert.Equal(HttpStatusCode.Created, (await b.Client.PostAsJsonAsync($"/api/group-tasks/{taskB.Id}/change-requests",
                new GroupChangeRequestCreateDto { NewDeadline = deadline.AddDays(3), Reason = "lần hai" })).StatusCode);

            Assert.Equal(HttpStatusCode.BadRequest, (await leader.Client.GetAsync($"/api/groups/{group.Id}/change-requests?status=Bogus")).StatusCode);
        }

        [Fact]
        public async Task ChangeRequest_ApproveIntoABusyTime_Returns409_UntilConfirmed()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            await CreateTaskAsync(leader, group.Id, new GroupTaskRequestDto { Title = "Đã có giờ", AssigneeId = a.UserId, FixedStart = Nine, FixedEnd = Nine.AddHours(1) });
            var meeting = await CreateTaskAsync(leader, group.Id, new GroupTaskRequestDto { Title = "Họp", AssigneeId = a.UserId, FixedStart = Nine.AddHours(4), FixedEnd = Nine.AddHours(5) });

            var request = (await (await a.Client.PostAsJsonAsync($"/api/group-tasks/{meeting.Id}/change-requests", new GroupChangeRequestCreateDto
            {
                NewFixedStart = Nine.AddMinutes(15),
                NewFixedEnd = Nine.AddMinutes(75),
                Reason = "Dời sớm"
            })).Content.ReadFromJsonAsync<GroupChangeRequestDto>())!;

            var blocked = await leader.Client.PostAsJsonAsync($"/api/group-change-requests/{request.Id}/approve", new GroupChangeDecisionDto());
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal("GROUP_ASSIGNEE_BUSY", await CodeOfAsync(blocked));
            Assert.Equal(Nine.AddHours(4), (await GetTaskAsync(leader, meeting.Id)).FixedStart);
            Assert.Equal("Pending", (await leader.Client.GetFromJsonAsync<List<GroupChangeRequestDto>>($"/api/groups/{group.Id}/change-requests"))!.Single().Status);

            var confirmed = await leader.Client.PostAsJsonAsync($"/api/group-change-requests/{request.Id}/approve", new GroupChangeDecisionDto { ConfirmConflicts = true });
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
            Assert.Equal(Nine.AddMinutes(15), (await GetTaskAsync(leader, meeting.Id)).FixedStart);
        }

        [Fact]
        public async Task ChangeRequest_SubTaskAssigneeCanPropose_AndClosedTaskCannot()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            var deadline = new DateTimeOffset(2030, 6, 1, 17, 0, 0, TimeSpan.Zero);
            var task = await CreateTaskAsync(leader, group.Id, new GroupTaskRequestDto { Title = "Việc", Deadline = deadline });
            await CreateSubTaskAsync(leader, task.Id, "Phần của A", a.UserId);

            var created = await a.Client.PostAsJsonAsync($"/api/group-tasks/{task.Id}/change-requests",
                new GroupChangeRequestCreateDto { NewDeadline = deadline.AddDays(1), Reason = "Phần của tôi nặng" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var request = (await created.Content.ReadFromJsonAsync<GroupChangeRequestDto>())!;
            await a.Client.PostAsync($"/api/group-change-requests/{request.Id}/cancel", null);

            await leader.Client.PutAsJsonAsync($"/api/group-tasks/{task.Id}/status", new GroupTaskStatusRequestDto { Status = "Skipped" });
            var closed = await a.Client.PostAsJsonAsync($"/api/group-tasks/{task.Id}/change-requests",
                new GroupChangeRequestCreateDto { NewDeadline = deadline.AddDays(2), Reason = "muộn rồi" });
            Assert.Equal(HttpStatusCode.BadRequest, closed.StatusCode);
        }
    }
}
