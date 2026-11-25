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
using DomainEntities = METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // Member tự xếp lịch cho việc nhóm (dùng chung ScheduleSlot), dọn slot khi giao lại việc, và tín hiệu trong báo cáo tiến độ
    // (docs/GROUP_FEATURE.md mục 13 và 14)
    public class GroupScheduleTests
    {
        private static readonly DateTimeOffset Nine = new(2030, 3, 1, 9, 0, 0, TimeSpan.Zero);

        private sealed record Actor(HttpClient Client, Guid UserId, string Email);

        private static async Task<Actor> LoginAsync(ApiFactory factory, string subject)
        {
            var (client, auth) = await TestHelpers.LoginAsync(factory, subject);
            return new Actor(client, auth.UserId, auth.Email);
        }

        private static async Task<GroupDto> CreateGroupAsync(Actor leader)
        {
            var response = await leader.Client.PostAsJsonAsync("/api/groups", new GroupRequestDto { Name = "Nhóm đồ án" });
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

        private static async Task<GroupTaskDto> CreateTaskAsync(Actor leader, Guid groupId, string title, Guid? assigneeId, DateTimeOffset? deadline = null, int? estimated = null)
        {
            var response = await leader.Client.PostAsJsonAsync($"/api/groups/{groupId}/tasks",
                new GroupTaskRequestDto { Title = title, AssigneeId = assigneeId, Deadline = deadline, EstimatedMinutes = estimated });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<GroupTaskDto>())!;
        }

        private static async Task<GroupSubTaskDto> CreateSubTaskAsync(Actor leader, Guid taskId, string title, Guid? assigneeId = null)
        {
            var response = await leader.Client.PostAsJsonAsync($"/api/group-tasks/{taskId}/subtasks",
                new GroupSubTaskRequestDto { Title = title, AssigneeId = assigneeId });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<GroupSubTaskDto>())!;
        }

        private static GroupSlotRequestDto Slot(int hour, int minutes = 60, Guid? subTaskId = null, string? status = null, int day = 1) => new()
        {
            SubTaskId = subTaskId,
            StartTime = new DateTimeOffset(2030, 3, day, hour, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2030, 3, day, hour, 0, 0, TimeSpan.Zero).AddMinutes(minutes),
            Status = status
        };

        private static Task<HttpResponseMessage> CreateSlotAsync(Actor actor, Guid taskId, GroupSlotRequestDto request) =>
            actor.Client.PostAsJsonAsync($"/api/group-tasks/{taskId}/schedule-slots", request);

        private static async Task<GroupSlotDto> CreateSlotOkAsync(Actor actor, Guid taskId, GroupSlotRequestDto request)
        {
            var response = await CreateSlotAsync(actor, taskId, request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<GroupSlotDto>())!;
        }

        private static Task<HttpResponseMessage> StartAsync(Actor actor, Guid taskId, Guid? subTaskId = null, Guid? slotId = null) =>
            actor.Client.PostAsJsonAsync($"/api/group-tasks/{taskId}/focus/start", new GroupFocusStartRequestDto { SubTaskId = subTaskId, ScheduleSlotId = slotId });

        private static Task<HttpResponseMessage> StopAsync(Actor actor, Guid sessionId, string reason, string? note = null) =>
            actor.Client.PostAsJsonAsync($"/api/group-focus/{sessionId}/stop", new { stopReason = reason, note });

        private static async Task<string> CodeOfAsync(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() ?? string.Empty : string.Empty;
        }

        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static Task<ScheduleSlot> SlotOf(ApiFactory factory, Guid slotId) =>
            QueryAsync(factory, db => db.ScheduleSlots.AsNoTracking().SingleAsync(s => s.Id == slotId));

        private static async Task<(Actor Leader, Actor A, Actor B, GroupDto Group)> SetupAsync(ApiFactory factory)
        {
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var b = await LoginAsync(factory, "b");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            await JoinAsync(leader, b, group.Id, "Member");
            return (leader, a, b, group);
        }

        // Slot cá nhân thật (dự án, việc cá nhân) để thử luật chồng giờ giữa việc nhóm và việc cá nhân
        private static async Task CreatePersonalSlotAsync(ApiFactory factory, Actor actor, int hour)
        {
            var domainId = Guid.NewGuid();
            await factory.SeedAsync(db => db.Domains.Add(new DomainEntities.Domain { Id = domainId, Name = $"Dev-{Guid.NewGuid()}", IsActive = true }));
            var project = await (await actor.Client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Cá nhân của tôi" })).Content.ReadFromJsonAsync<ProjectDto>();
            var task = await (await actor.Client.PostAsJsonAsync($"/api/projects/{project!.Id}/tasks", new TaskRequestDto
            {
                Title = "Việc riêng", TaskType = "Flexible", DomainId = domainId, Deadline = new DateTimeOffset(2030, 3, 30, 17, 0, 0, TimeSpan.Zero), EstimatedMinutes = 60
            })).Content.ReadFromJsonAsync<TaskDto>();
            var start = new DateTimeOffset(2030, 3, 1, hour, 0, 0, TimeSpan.Zero);
            var response = await actor.Client.PostAsJsonAsync($"/api/tasks/{task!.Id}/schedule-slots",
                new ScheduleSlotRequestDto { StartTime = start, EndTime = start.AddMinutes(60), Status = "Approved", IsManualOverride = true });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        // ================================================================ Tạo slot

        [Fact]
        public async Task CreateSlot_ForTheTaskAssignedToMe_CreatesAGroupSlot_ThatShowsOnMyCalendarOnly()
        {
            using var factory = new ApiFactory();
            var (leader, a, _, group) = await SetupAsync(factory);
            var task = await CreateTaskAsync(leader, group.Id, "Viết báo cáo", a.UserId);

            var slot = await CreateSlotOkAsync(a, task.Id, Slot(9));

            Assert.Equal(task.Id, slot.GroupTaskId);
            Assert.Null(slot.TaskId);
            Assert.Equal(a.UserId, slot.UserId);
            Assert.Equal("Approved", slot.Status);
            Assert.Equal(60, slot.ProposedMinutes);
            Assert.Equal("Viết báo cáo", slot.TaskTitle);
            Assert.Equal(group.Id, slot.GroupId);

            var range = "?from=2030-02-28T00:00:00Z&to=2030-03-03T00:00:00Z";
            var mine = (await a.Client.GetFromJsonAsync<CalendarDto>($"/api/calendar{range}"))!;
            var item = Assert.Single(mine.Items, i => i.Source == "Slot");
            Assert.Equal(group.Id, item.GroupId);
            Assert.Equal(task.Id, item.GroupTaskId);
            Assert.Null(item.TaskId);
            Assert.Equal("Viết báo cáo", item.Title);
            Assert.Empty((await leader.Client.GetFromJsonAsync<CalendarDto>($"/api/calendar{range}"))!.Items);
        }

        [Fact]
        public async Task CreateSlot_ByAnyoneExceptTheAssignee_IsRejected()
        {
            using var factory = new ApiFactory();
            var (leader, a, b, group) = await SetupAsync(factory);
            var viewer = await LoginAsync(factory, "viewer");
            var outsider = await LoginAsync(factory, "outsider");
            await JoinAsync(leader, viewer, group.Id, "Viewer");
            var task = await CreateTaskAsync(leader, group.Id, "Việc của A", a.UserId);

            Assert.Equal(HttpStatusCode.Forbidden, (await CreateSlotAsync(b, task.Id, Slot(9))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await CreateSlotAsync(leader, task.Id, Slot(9))).StatusCode);   // Leader không xếp hộ
            Assert.Equal(HttpStatusCode.Forbidden, (await CreateSlotAsync(viewer, task.Id, Slot(9))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await CreateSlotAsync(outsider, task.Id, Slot(9))).StatusCode);
            Assert.Equal(0, await QueryAsync(factory, db => db.ScheduleSlots.CountAsync()));
        }

        [Fact]
        public async Task CreateSlot_ForATaskWithSubTasks_NeedsTheSubTask_AndTheSubTaskMustBeMine()
        {
            using var factory = new ApiFactory();
            var (leader, a, b, group) = await SetupAsync(factory);
            var task = await CreateTaskAsync(leader, group.Id, "Việc lớn", a.UserId);
            var mine = await CreateSubTaskAsync(leader, task.Id, "Phần của tôi");               // chưa giao riêng: theo người được giao việc cha
            var theirs = await CreateSubTaskAsync(leader, task.Id, "Phần của B", b.UserId);

            Assert.Equal(HttpStatusCode.BadRequest, (await CreateSlotAsync(a, task.Id, Slot(9))).StatusCode);                     // phải chọn việc con
            Assert.Equal(HttpStatusCode.Forbidden, (await CreateSlotAsync(a, task.Id, Slot(9, subTaskId: theirs.Id))).StatusCode);
            var slot = await CreateSlotOkAsync(a, task.Id, Slot(9, subTaskId: mine.Id));
            var bSlot = await CreateSlotOkAsync(b, task.Id, Slot(11, subTaskId: theirs.Id));

            Assert.Equal(mine.Id, slot.GroupSubTaskId);
            Assert.Equal("Phần của tôi", slot.SubTaskTitle);
            Assert.Equal(theirs.Id, bSlot.GroupSubTaskId);
        }

        [Fact]
        public async Task CreateSlot_ForAnArchivedGroup_ClosedTaskOrBadTimes_Returns400()
        {
            using var factory = new ApiFactory();
            var (leader, a, _, group) = await SetupAsync(factory);
            var done = await CreateTaskAsync(leader, group.Id, "Đã xong", a.UserId);
            var open = await CreateTaskAsync(leader, group.Id, "Đang mở", a.UserId);
            await leader.Client.PutAsJsonAsync($"/api/group-tasks/{done.Id}/status", new GroupTaskStatusRequestDto { Status = "Done" });

            var backwards = Slot(10);
            backwards.EndTime = backwards.StartTime.AddMinutes(-5);
            Assert.Equal(HttpStatusCode.BadRequest, (await CreateSlotAsync(a, done.Id, Slot(9))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await CreateSlotAsync(a, open.Id, backwards)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await CreateSlotAsync(a, open.Id, Slot(9, status: "Done"))).StatusCode);

            await leader.Client.PostAsync($"/api/groups/{group.Id}/archive", null);
            Assert.Equal(HttpStatusCode.BadRequest, (await CreateSlotAsync(a, open.Id, Slot(9))).StatusCode);
        }

        [Fact]
        public async Task CreateSlot_OverlappingMyOtherActiveSlots_IsBlocked_IncludingPersonalSlots()
        {
            using var factory = new ApiFactory();
            var (leader, a, _, group) = await SetupAsync(factory);
            var t1 = await CreateTaskAsync(leader, group.Id, "Việc 1", a.UserId);
            var t2 = await CreateTaskAsync(leader, group.Id, "Việc 2", a.UserId);
            await CreateSlotOkAsync(a, t1.Id, Slot(9));                  // 9h-10h
            await CreatePersonalSlotAsync(factory, a, 14);               // 14h-15h (việc cá nhân)

            var groupClash = await CreateSlotAsync(a, t2.Id, new GroupSlotRequestDto { StartTime = Nine.AddMinutes(30), EndTime = Nine.AddMinutes(90) });
            var personalClash = await CreateSlotAsync(a, t2.Id, Slot(14, 30));

            Assert.Equal(HttpStatusCode.Conflict, groupClash.StatusCode);
            Assert.Equal("SLOT_OVERLAP", await CodeOfAsync(groupClash));
            Assert.Equal(HttpStatusCode.Conflict, personalClash.StatusCode);
            await CreateSlotOkAsync(a, t2.Id, Slot(10));                 // chạm đầu mút thì được
            await CreateSlotOkAsync(a, t2.Id, Slot(9, 30, status: "Proposed").Also(s => s.StartTime = s.StartTime.AddMinutes(10)));   // Proposed không chặn
        }

        // ================================================================ Xem, sửa, hủy slot

        [Fact]
        public async Task Slots_AreVisibleToEveryMember_EditableOnlyByTheirOwner_AndCancelHidesThem()
        {
            using var factory = new ApiFactory();
            var (leader, a, b, group) = await SetupAsync(factory);
            var task = await CreateTaskAsync(leader, group.Id, "Việc", a.UserId);
            var slot = await CreateSlotOkAsync(a, task.Id, Slot(9));

            var seenByLeader = await leader.Client.GetFromJsonAsync<List<GroupSlotDto>>($"/api/group-tasks/{task.Id}/schedule-slots");
            Assert.Equal(slot.Id, Assert.Single(seenByLeader!).Id);
            Assert.Equal(a.UserId, seenByLeader![0].UserId);

            var edit = new ScheduleSlotRequestDto { StartTime = Nine.AddHours(2), EndTime = Nine.AddHours(3), IsManualOverride = true };
            Assert.Equal(HttpStatusCode.NotFound, (await b.Client.PutAsJsonAsync($"/api/schedule-slots/{slot.Id}", edit)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await leader.Client.DeleteAsync($"/api/schedule-slots/{slot.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await a.Client.PutAsJsonAsync($"/api/schedule-slots/{slot.Id}", edit)).StatusCode);
            Assert.Equal(Nine.AddHours(2).UtcDateTime, (await SlotOf(factory, slot.Id)).StartTime);

            Assert.Equal(HttpStatusCode.OK, (await a.Client.DeleteAsync($"/api/schedule-slots/{slot.Id}")).StatusCode);
            Assert.Equal("Cancelled", (await SlotOf(factory, slot.Id)).Status);
            Assert.Empty((await leader.Client.GetFromJsonAsync<List<GroupSlotDto>>($"/api/group-tasks/{task.Id}/schedule-slots"))!);
            Assert.Single((await leader.Client.GetFromJsonAsync<List<GroupSlotDto>>($"/api/group-tasks/{task.Id}/schedule-slots?includeCancelled=true"))!);
        }

        [Fact]
        public async Task MovingASlotOntoAnotherActiveSlot_Returns409_AndKeepsTheOldTime()
        {
            using var factory = new ApiFactory();
            var (leader, a, _, group) = await SetupAsync(factory);
            var task = await CreateTaskAsync(leader, group.Id, "Việc", a.UserId);
            await CreateSlotOkAsync(a, task.Id, Slot(7));
            var other = await CreateSlotOkAsync(a, task.Id, Slot(9));

            var response = await a.Client.PutAsJsonAsync($"/api/schedule-slots/{other.Id}",
                new ScheduleSlotRequestDto { StartTime = Nine.AddHours(-1.5), EndTime = Nine.AddHours(0), IsManualOverride = true });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(Nine.UtcDateTime, (await SlotOf(factory, other.Id)).StartTime);
        }

        // ================================================================ Bấm giờ theo slot

        [Fact]
        public async Task Start_FromASlot_MovesItToInProgress_AndStopDecidesItsNextStatus()
        {
            using var factory = new ApiFactory();
            var (leader, a, _, group) = await SetupAsync(factory);
            var task = await CreateTaskAsync(leader, group.Id, "Việc", a.UserId);
            var slot = await CreateSlotOkAsync(a, task.Id, Slot(9));

            var started = await StartAsync(a, task.Id, slotId: slot.Id);
            var startBody = await started.Content.ReadFromJsonAsync<GroupFocusStartResponseDto>();
            Assert.Equal(HttpStatusCode.Created, started.StatusCode);
            Assert.Equal(slot.Id, startBody!.ScheduleSlotId);
            Assert.Equal("InProgress", (await SlotOf(factory, slot.Id)).Status);

            Assert.Equal(HttpStatusCode.OK, (await StopAsync(a, startBody.SessionId, "Interrupted")).StatusCode);
            Assert.Equal("Due", (await SlotOf(factory, slot.Id)).Status);                      // gián đoạn: làm tiếp được

            var again = await (await StartAsync(a, task.Id, slotId: slot.Id)).Content.ReadFromJsonAsync<GroupFocusStartResponseDto>();
            Assert.Equal(HttpStatusCode.OK, (await StopAsync(a, again!.SessionId, "Completed")).StatusCode);
            Assert.Equal("Done", (await SlotOf(factory, slot.Id)).Status);
            Assert.Equal(slot.Id, (await QueryAsync(factory, db => db.GroupFocusSessions.AsNoTracking().OrderBy(s => s.StartedAt).Select(s => s.ScheduleSlotId).LastAsync())));
        }

        [Fact]
        public async Task Start_WithAWrongSlot_IsRejected_AndStartWithoutSlotStillWorks()
        {
            using var factory = new ApiFactory();
            var (leader, a, b, group) = await SetupAsync(factory);
            var task = await CreateTaskAsync(leader, group.Id, "Việc A", a.UserId);
            var other = await CreateTaskAsync(leader, group.Id, "Việc khác của A", a.UserId);
            var bTask = await CreateTaskAsync(leader, group.Id, "Việc B", b.UserId);
            var ofOtherTask = await CreateSlotOkAsync(a, other.Id, Slot(9));
            var proposed = await CreateSlotOkAsync(a, task.Id, Slot(11, status: "Proposed"));
            var bSlot = await CreateSlotOkAsync(b, bTask.Id, Slot(13));

            Assert.Equal(HttpStatusCode.BadRequest, (await StartAsync(a, task.Id, slotId: ofOtherTask.Id)).StatusCode);   // sai việc
            Assert.Equal(HttpStatusCode.BadRequest, (await StartAsync(a, task.Id, slotId: proposed.Id)).StatusCode);      // chưa duyệt
            Assert.Equal(HttpStatusCode.NotFound, (await StartAsync(a, task.Id, slotId: bSlot.Id)).StatusCode);           // slot của người khác
            Assert.Equal(HttpStatusCode.Created, (await StartAsync(a, task.Id)).StatusCode);                              // không gắn slot vẫn được
        }

        // ================================================================ Giao lại việc, rời nhóm: dọn slot (phương án A)

        [Fact]
        public async Task ReassigningATask_CancelsThePreviousAssigneesUnstartedSlots_KeepsHistory_AndNotifiesThem()
        {
            using var factory = new ApiFactory();
            var (leader, a, b, group) = await SetupAsync(factory);
            var task = await CreateTaskAsync(leader, group.Id, "Việc bàn giao", a.UserId);
            var future = await CreateSlotOkAsync(a, task.Id, Slot(9));
            var proposed = await CreateSlotOkAsync(a, task.Id, Slot(11, status: "Proposed"));
            var finished = await CreateSlotOkAsync(a, task.Id, Slot(13));
            await factory.SeedAsync(db => db.ScheduleSlots.Single(s => s.Id == finished.Id).Status = "Missed");   // lịch sử giữ nguyên

            var response = await leader.Client.PutAsJsonAsync($"/api/group-tasks/{task.Id}/assignee", new GroupAssigneeRequestDto { AssigneeId = b.UserId });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Cancelled", (await SlotOf(factory, future.Id)).Status);
            Assert.Equal("Cancelled", (await SlotOf(factory, proposed.Id)).Status);
            Assert.Equal("Missed", (await SlotOf(factory, finished.Id)).Status);
            Assert.Equal(2, await QueryAsync(factory, db => db.ScheduleChangeLogs.CountAsync(l => l.ChangeType == "Deleted")));

            var note = Assert.Single(await QueryAsync(factory, db => db.Notifications.AsNoTracking()
                .Where(n => n.UserId == a.UserId && n.Type == "GroupStatusChanged" && n.GroupTaskId == task.Id).ToListAsync()));
            Assert.Contains("2 slot", note.Message);

            Assert.Empty((await b.Client.GetFromJsonAsync<List<GroupSlotDto>>($"/api/group-tasks/{task.Id}/schedule-slots"))!.Where(s => s.UserId == b.UserId));
            await CreateSlotOkAsync(b, task.Id, Slot(9));                               // người mới tự xếp lịch riêng
            Assert.Equal(HttpStatusCode.Forbidden, (await CreateSlotAsync(a, task.Id, Slot(15))).StatusCode);   // người cũ không xếp tiếp được
        }

        [Fact]
        public async Task ReassigningATask_StopsThePreviousAssigneesRunningSession_AndCancelsTheRunningSlot()
        {
            using var factory = new ApiFactory();
            var (leader, a, b, group) = await SetupAsync(factory);
            var task = await CreateTaskAsync(leader, group.Id, "Việc đang làm", a.UserId);
            var slot = await CreateSlotOkAsync(a, task.Id, Slot(9));
            var session = await (await StartAsync(a, task.Id, slotId: slot.Id)).Content.ReadFromJsonAsync<GroupFocusStartResponseDto>();

            await leader.Client.PutAsJsonAsync($"/api/group-tasks/{task.Id}/assignee", new GroupAssigneeRequestDto { AssigneeId = b.UserId });

            var stored = await QueryAsync(factory, db => db.GroupFocusSessions.AsNoTracking().SingleAsync(s => s.Id == session!.SessionId));
            Assert.NotNull(stored.EndedAt);
            Assert.Equal("Other", stored.StopReason);
            Assert.Equal("Cancelled", (await SlotOf(factory, slot.Id)).Status);
        }

        [Fact]
        public async Task ReassigningASubTask_OnlyAffectsThatSubTasksSlots()
        {
            using var factory = new ApiFactory();
            var (leader, a, b, group) = await SetupAsync(factory);
            var task = await CreateTaskAsync(leader, group.Id, "Việc lớn", a.UserId);
            var s1 = await CreateSubTaskAsync(leader, task.Id, "Phần 1", a.UserId);
            var s2 = await CreateSubTaskAsync(leader, task.Id, "Phần 2", a.UserId);
            var slot1 = await CreateSlotOkAsync(a, task.Id, Slot(9, subTaskId: s1.Id));
            var slot2 = await CreateSlotOkAsync(a, task.Id, Slot(11, subTaskId: s2.Id));

            var response = await leader.Client.PutAsJsonAsync($"/api/group-subtasks/{s1.Id}/assignee", new GroupAssigneeRequestDto { AssigneeId = b.UserId });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Cancelled", (await SlotOf(factory, slot1.Id)).Status);
            Assert.Equal("Approved", (await SlotOf(factory, slot2.Id)).Status);
        }

        [Fact]
        public async Task ReassigningTheParentTask_DoesNotTouchSlotsOfSubTasksAssignedToAnotherPerson()
        {
            using var factory = new ApiFactory();
            var (leader, a, b, group) = await SetupAsync(factory);
            var task = await CreateTaskAsync(leader, group.Id, "Việc lớn", a.UserId);
            var own = await CreateSubTaskAsync(leader, task.Id, "Của B", b.UserId);
            var inherited = await CreateSubTaskAsync(leader, task.Id, "Chưa giao riêng");
            var bSlot = await CreateSlotOkAsync(b, task.Id, Slot(9, subTaskId: own.Id));
            var aSlot = await CreateSlotOkAsync(a, task.Id, Slot(11, subTaskId: inherited.Id));

            await leader.Client.PutAsJsonAsync($"/api/group-tasks/{task.Id}/assignee", new GroupAssigneeRequestDto { AssigneeId = b.UserId });

            Assert.Equal("Approved", (await SlotOf(factory, bSlot.Id)).Status);   // việc con giao riêng cho B không đổi
            Assert.Equal("Cancelled", (await SlotOf(factory, aSlot.Id)).Status);  // việc con thừa hưởng người của việc cha: A mất
        }

        [Fact]
        public async Task LeavingTheGroup_RemovingAMember_OrDemotingToViewer_CancelsTheirUnstartedSlots()
        {
            using var factory = new ApiFactory();
            var (leader, a, b, group) = await SetupAsync(factory);
            var c = await LoginAsync(factory, "c");
            await JoinAsync(leader, c, group.Id, "Member");
            var tA = await CreateTaskAsync(leader, group.Id, "Việc A", a.UserId);
            var tB = await CreateTaskAsync(leader, group.Id, "Việc B", b.UserId);
            var tC = await CreateTaskAsync(leader, group.Id, "Việc C", c.UserId);
            var sA = await CreateSlotOkAsync(a, tA.Id, Slot(9));
            var sB = await CreateSlotOkAsync(b, tB.Id, Slot(9));
            var sC = await CreateSlotOkAsync(c, tC.Id, Slot(9));

            Assert.Equal(HttpStatusCode.NoContent, (await a.Client.PostAsync($"/api/groups/{group.Id}/leave", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await leader.Client.DeleteAsync($"/api/groups/{group.Id}/members/{b.UserId}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await leader.Client.PutAsJsonAsync($"/api/groups/{group.Id}/members/{c.UserId}", new GroupMemberRoleRequestDto { Role = "Viewer" })).StatusCode);

            Assert.Equal("Cancelled", (await SlotOf(factory, sA.Id)).Status);
            Assert.Equal("Cancelled", (await SlotOf(factory, sB.Id)).Status);
            Assert.Equal("Cancelled", (await SlotOf(factory, sC.Id)).Status);
        }

        [Fact]
        public async Task DeletingATask_DeletesItsSlots()
        {
            using var factory = new ApiFactory();
            var (leader, a, _, group) = await SetupAsync(factory);
            var task = await CreateTaskAsync(leader, group.Id, "Việc sẽ xóa", a.UserId);
            var slot = await CreateSlotOkAsync(a, task.Id, Slot(9));
            await a.Client.PutAsJsonAsync($"/api/schedule-slots/{slot.Id}", new ScheduleSlotRequestDto { StartTime = Nine.AddHours(2), EndTime = Nine.AddHours(3), IsManualOverride = true });

            Assert.Equal(HttpStatusCode.NoContent, (await leader.Client.DeleteAsync($"/api/group-tasks/{task.Id}")).StatusCode);

            Assert.Equal(0, await QueryAsync(factory, db => db.ScheduleSlots.CountAsync()));
            Assert.Equal(0, await QueryAsync(factory, db => db.ScheduleChangeLogs.CountAsync()));
        }

        // ================================================================ Tín hiệu trong báo cáo tiến độ

        private static async Task<GroupProgressDto> ProgressAsync(Actor actor, Guid groupId) =>
            (await actor.Client.GetFromJsonAsync<GroupProgressDto>($"/api/groups/{groupId}/progress"))!;

        [Fact]
        public async Task Signals_OverdueTasks_AreCountedPerMember_ButUnassignedAndClosedTasksAreIgnored()
        {
            using var factory = new ApiFactory();
            var (leader, a, _, group) = await SetupAsync(factory);
            var deadline = new DateTimeOffset(2030, 3, 5, 17, 0, 0, TimeSpan.Zero);
            var late = await CreateTaskAsync(leader, group.Id, "Trễ hạn", a.UserId, deadline);
            await CreateTaskAsync(leader, group.Id, "Chưa giao", null, deadline);
            var done = await CreateTaskAsync(leader, group.Id, "Đã xong", a.UserId, deadline);
            await leader.Client.PutAsJsonAsync($"/api/group-tasks/{done.Id}/status", new GroupTaskStatusRequestDto { Status = "Done" });
            factory.Clock.Set(new DateTimeOffset(2030, 3, 10, 9, 0, 0, TimeSpan.Zero));

            var progress = await ProgressAsync(leader, group.Id);

            Assert.Equal(1, progress.Signals.OverdueTasks);
            var signal = Assert.Single(progress.Signals.Tasks, t => t.IsOverdue);
            Assert.Equal(late.Id, signal.TaskId);
            Assert.Equal(a.UserId, signal.AssigneeId);
            Assert.Equal(1, progress.Members.Single(m => m.UserId == a.UserId).OverdueTasks);
            Assert.Equal(0, progress.Members.Single(m => m.UserId == leader.UserId).OverdueTasks);
        }

        [Fact]
        public async Task Signals_UnscheduledTasks_BecomeScheduledOnceTheAssigneePlansASlot_AndCancelledSlotsDoNotCount()
        {
            using var factory = new ApiFactory();
            var (leader, a, _, group) = await SetupAsync(factory);
            var task = await CreateTaskAsync(leader, group.Id, "Cần lịch", a.UserId);

            var before = await ProgressAsync(leader, group.Id);
            Assert.Equal(1, before.Signals.UnscheduledTasks);
            Assert.False(before.Signals.Tasks.Single().HasSchedule);
            Assert.Equal(1, before.Members.Single(m => m.UserId == a.UserId).UnscheduledTasks);

            var slot = await CreateSlotOkAsync(a, task.Id, Slot(9));
            var scheduled = await ProgressAsync(leader, group.Id);
            Assert.Equal(0, scheduled.Signals.UnscheduledTasks);
            Assert.DoesNotContain(scheduled.Signals.Tasks, t => !t.HasSchedule);

            await a.Client.DeleteAsync($"/api/schedule-slots/{slot.Id}");
            Assert.Equal(1, (await ProgressAsync(leader, group.Id)).Signals.UnscheduledTasks);
        }

        [Fact]
        public async Task Signals_AtRisk_WhenTheDeadlineIsNearAndTheScheduledMinutesDoNotCoverTheRemainingWork()
        {
            using var factory = new ApiFactory();
            var (leader, a, _, group) = await SetupAsync(factory);
            factory.Clock.Set(new DateTimeOffset(2030, 3, 1, 8, 0, 0, TimeSpan.Zero));
            var task = await CreateTaskAsync(leader, group.Id, "Gần deadline", a.UserId, new DateTimeOffset(2030, 3, 3, 17, 0, 0, TimeSpan.Zero), estimated: 120);
            var far = await CreateTaskAsync(leader, group.Id, "Còn xa", a.UserId, new DateTimeOffset(2030, 3, 20, 17, 0, 0, TimeSpan.Zero), estimated: 120);
            await CreateSlotOkAsync(a, task.Id, Slot(9, 60, day: 2));            // 60 phút trước deadline: chưa đủ 120

            var risky = await ProgressAsync(leader, group.Id);
            var signal = risky.Signals.Tasks.Single(t => t.TaskId == task.Id);
            Assert.True(signal.AtRisk);
            Assert.Equal(60, signal.ScheduledMinutes);
            Assert.Equal(120, signal.EstimatedMinutes);
            Assert.DoesNotContain(risky.Signals.Tasks, t => t.TaskId == far.Id && t.AtRisk);   // deadline còn xa: chưa đánh giá
            Assert.Equal(1, risky.Signals.AtRiskTasks);

            await CreateSlotOkAsync(a, task.Id, Slot(11, 60, day: 2));           // đủ 120 phút
            Assert.False((await ProgressAsync(leader, group.Id)).Signals.Tasks.Any(t => t.TaskId == task.Id && t.AtRisk));
        }

        [Fact]
        public async Task Signals_Stale_WhenNothingHappenedForDays_ButNotForAFreshTask()
        {
            using var factory = new ApiFactory();
            var (leader, a, _, group) = await SetupAsync(factory);
            await CreateTaskAsync(leader, group.Id, "Việc mới giao", a.UserId);

            Assert.Equal(0, (await ProgressAsync(leader, group.Id)).Signals.StaleTasks);

            factory.Clock.Advance(TimeSpan.FromDays(4));
            var later = await ProgressAsync(leader, group.Id);

            Assert.Equal(1, later.Signals.StaleTasks);
            Assert.True(later.Signals.Tasks.Single().IsStale);
            Assert.Equal(1, later.Members.Single(m => m.UserId == a.UserId).StaleTasks);
        }

        [Fact]
        public async Task Signals_DoNotExposeAnyPersonalCalendarDetails()
        {
            using var factory = new ApiFactory();
            var (leader, a, _, group) = await SetupAsync(factory);
            var task = await CreateTaskAsync(leader, group.Id, "Việc nhóm", a.UserId);
            await CreatePersonalSlotAsync(factory, a, 14);
            await CreateSlotOkAsync(a, task.Id, Slot(9));

            var response = await leader.Client.GetAsync($"/api/groups/{group.Id}/progress");
            var raw = await response.Content.ReadAsStringAsync();

            Assert.DoesNotContain("Việc riêng", raw);
            Assert.DoesNotContain("Cá nhân của tôi", raw);
        }
    }

    internal static class TestExtensions
    {
        public static T Also<T>(this T value, Action<T> action)
        {
            action(value);
            return value;
        }
    }
}
