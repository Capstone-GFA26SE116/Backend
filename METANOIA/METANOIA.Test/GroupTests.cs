using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Test.Infrastructure;
using Xunit;

namespace METANOIA.Test
{
    // Nhánh Group (docs/GROUP_FEATURE.md). Mỗi test dùng một ApiFactory (database InMemory riêng).
    public class GroupTests
    {
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

        // Leader mời, người được mời chấp nhận qua danh sách lời mời của mình
        private static async Task JoinAsync(Actor leader, Actor invitee, Guid groupId, string role)
        {
            var invite = await leader.Client.PostAsJsonAsync($"/api/groups/{groupId}/invitations",
                new GroupInvitationRequestDto { Email = invitee.Email, Role = role });
            Assert.Equal(HttpStatusCode.Created, invite.StatusCode);

            var mine = await invitee.Client.GetFromJsonAsync<List<GroupInvitationDto>>("/api/group-invitations");
            var invitation = Assert.Single(mine!, i => i.GroupId == groupId);
            var accepted = await invitee.Client.PostAsync($"/api/group-invitations/{invitation.Id}/accept", null);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        private static async Task<GroupTaskDto> CreateTaskAsync(Actor actor, Guid groupId, string title = "Viết báo cáo", Guid? assigneeId = null, Guid? milestoneId = null)
        {
            var response = await actor.Client.PostAsJsonAsync($"/api/groups/{groupId}/tasks",
                new GroupTaskRequestDto { Title = title, AssigneeId = assigneeId, MilestoneId = milestoneId });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<GroupTaskDto>())!;
        }

        private static async Task<GroupSubTaskDto> CreateSubTaskAsync(Actor actor, Guid taskId, string title, Guid? assigneeId = null)
        {
            var response = await actor.Client.PostAsJsonAsync($"/api/group-tasks/{taskId}/subtasks",
                new GroupSubTaskRequestDto { Title = title, AssigneeId = assigneeId });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<GroupSubTaskDto>())!;
        }

        private static Task<HttpResponseMessage> SetSubTaskStatusAsync(Actor actor, Guid subTaskId, string status) =>
            actor.Client.PutAsJsonAsync($"/api/group-subtasks/{subTaskId}/status", new GroupTaskStatusRequestDto { Status = status });

        private static Task<HttpResponseMessage> SetTaskStatusAsync(Actor actor, Guid taskId, string status) =>
            actor.Client.PutAsJsonAsync($"/api/group-tasks/{taskId}/status", new GroupTaskStatusRequestDto { Status = status });

        private static async Task<GroupTaskDto> GetTaskAsync(Actor actor, Guid taskId) =>
            (await actor.Client.GetFromJsonAsync<GroupTaskDto>($"/api/group-tasks/{taskId}"))!;

        // ================================================================ Nhóm và quyền

        [Fact]
        public async Task CreateGroup_MakesCreatorLeader_AndListsIt()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");

            var group = await CreateGroupAsync(leader);

            Assert.Equal("Leader", group.MyRole);
            Assert.Equal(1, group.MemberCount);
            Assert.Equal("Active", group.Status);

            var mine = await leader.Client.GetFromJsonAsync<List<GroupDto>>("/api/groups");
            Assert.Contains(mine!, g => g.Id == group.Id);
        }

        [Fact]
        public async Task CreateGroup_WithEmptyName_Returns400()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");

            var response = await leader.Client.PostAsJsonAsync("/api/groups", new GroupRequestDto { Name = " " });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task NonMember_Gets404_EverywhereInTheGroup()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var outsider = await LoginAsync(factory, "outsider");
            var group = await CreateGroupAsync(leader);
            var task = await CreateTaskAsync(leader, group.Id);

            Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.GetAsync($"/api/groups/{group.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.GetAsync($"/api/groups/{group.Id}/members")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.GetAsync($"/api/groups/{group.Id}/tasks")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.GetAsync($"/api/group-tasks/{task.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.GetAsync($"/api/groups/{group.Id}/progress")).StatusCode);

            var list = await outsider.Client.GetFromJsonAsync<List<GroupDto>>("/api/groups");
            Assert.Empty(list!);
        }

        [Fact]
        public async Task RoleMatrix_ViewerReadsOnly_MemberCannotManage_LeaderManages()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var member = await LoginAsync(factory, "member");
            var viewer = await LoginAsync(factory, "viewer");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, member, group.Id, "Member");
            await JoinAsync(leader, viewer, group.Id, "Viewer");
            var task = await CreateTaskAsync(leader, group.Id, assigneeId: member.UserId);

            // Viewer xem được nhưng không ghi được
            Assert.Equal(HttpStatusCode.OK, (await viewer.Client.GetAsync($"/api/group-tasks/{task.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync($"/api/group-tasks/{task.Id}/comments", new GroupCommentRequestDto { Content = "hi" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SetTaskStatusAsync(viewer, task.Id, "InProgress")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.GetAsync($"/api/groups/{group.Id}/activity")).StatusCode);

            // Member không tạo việc, không mời, không xóa thành viên, không tạo giai đoạn
            Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.PostAsJsonAsync($"/api/groups/{group.Id}/tasks", new GroupTaskRequestDto { Title = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.PostAsJsonAsync($"/api/groups/{group.Id}/invitations", new GroupInvitationRequestDto { Email = "a@b.test" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.DeleteAsync($"/api/groups/{group.Id}/members/{viewer.UserId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.PostAsJsonAsync($"/api/groups/{group.Id}/milestones", new GroupMilestoneRequestDto { Name = "M", OrderIndex = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.PostAsync($"/api/groups/{group.Id}/archive", null)).StatusCode);

            // Member bình luận được
            Assert.Equal(HttpStatusCode.Created, (await member.Client.PostAsJsonAsync($"/api/group-tasks/{task.Id}/comments", new GroupCommentRequestDto { Content = "ok" })).StatusCode);
        }

        // ================================================================ Lời mời và thành viên

        [Fact]
        public async Task Invitation_OnlyTheInvitedEmailCanAccept_AndCannotBeReused()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var invitee = await LoginAsync(factory, "invitee");
            var stranger = await LoginAsync(factory, "stranger");
            var group = await CreateGroupAsync(leader);

            var invite = await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/invitations",
                new GroupInvitationRequestDto { Email = invitee.Email.ToUpperInvariant() });
            var created = (await invite.Content.ReadFromJsonAsync<GroupInvitationCreatedDto>())!;

            Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
            Assert.Equal("Member", created.Role);
            Assert.False(string.IsNullOrEmpty(created.AcceptToken));

            // Người khác không dùng được lời mời (404, không lộ nội dung), kể cả có token
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.PostAsync($"/api/group-invitations/{created.Id}/accept", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.PostAsJsonAsync("/api/group-invitations/accept-by-token", new GroupInvitationTokenRequestDto { Token = created.AcceptToken })).StatusCode);

            var accepted = await invitee.Client.PostAsJsonAsync("/api/group-invitations/accept-by-token", new GroupInvitationTokenRequestDto { Token = created.AcceptToken });
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.Equal("Member", (await accepted.Content.ReadFromJsonAsync<GroupDto>())!.MyRole);

            // Dùng lại lời mời đã chấp nhận
            Assert.Equal(HttpStatusCode.BadRequest, (await invitee.Client.PostAsync($"/api/group-invitations/{created.Id}/accept", null)).StatusCode);

            var members = await leader.Client.GetFromJsonAsync<List<GroupMemberDto>>($"/api/groups/{group.Id}/members");
            Assert.Equal(2, members!.Count);
        }

        [Fact]
        public async Task Invitation_ToExistingMemberOrDuplicatePending_Returns400()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var member = await LoginAsync(factory, "member");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, member, group.Id, "Member");

            var again = await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/invitations", new GroupInvitationRequestDto { Email = member.Email });
            Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);

            var first = await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/invitations", new GroupInvitationRequestDto { Email = "new@test.com" });
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            var duplicate = await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/invitations", new GroupInvitationRequestDto { Email = "new@test.com" });
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

            var invalid = await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/invitations", new GroupInvitationRequestDto { Email = "khong-hop-le" });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        [Fact]
        public async Task Invitation_ExpiresAfterSevenDays_AndRevokedOneCannotBeAccepted()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var late = await LoginAsync(factory, "late");
            var revoked = await LoginAsync(factory, "revoked");
            var group = await CreateGroupAsync(leader);

            var lateInvite = await (await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/invitations", new GroupInvitationRequestDto { Email = late.Email })).Content.ReadFromJsonAsync<GroupInvitationCreatedDto>();
            var revokedInvite = await (await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/invitations", new GroupInvitationRequestDto { Email = revoked.Email })).Content.ReadFromJsonAsync<GroupInvitationCreatedDto>();

            Assert.Equal(HttpStatusCode.NoContent, (await leader.Client.DeleteAsync($"/api/groups/{group.Id}/invitations/{revokedInvite!.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await revoked.Client.PostAsync($"/api/group-invitations/{revokedInvite.Id}/accept", null)).StatusCode);

            factory.Clock.Advance(TimeSpan.FromDays(8));

            Assert.Empty((await late.Client.GetFromJsonAsync<List<GroupInvitationDto>>("/api/group-invitations"))!);
            Assert.Equal(HttpStatusCode.BadRequest, (await late.Client.PostAsync($"/api/group-invitations/{lateInvite!.Id}/accept", null)).StatusCode);
        }

        [Fact]
        public async Task Invitation_AfterExpiry_CanBeSentAgain_AndTheOldOneIsMarkedExpired()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var late = await LoginAsync(factory, "late");
            var group = await CreateGroupAsync(leader);

            var first = await (await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/invitations", new GroupInvitationRequestDto { Email = late.Email }))
                .Content.ReadFromJsonAsync<GroupInvitationCreatedDto>();
            factory.Clock.Advance(TimeSpan.FromDays(8));

            var again = await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/invitations", new GroupInvitationRequestDto { Email = late.Email });
            Assert.Equal(HttpStatusCode.Created, again.StatusCode);

            var list = (await leader.Client.GetFromJsonAsync<List<GroupInvitationDto>>($"/api/groups/{group.Id}/invitations"))!;
            Assert.Equal("Expired", list.Single(i => i.Id == first!.Id).Status);
            Assert.Equal(1, list.Count(i => i.Status == "Pending"));

            var mine = await late.Client.GetFromJsonAsync<List<GroupInvitationDto>>("/api/group-invitations");
            Assert.Equal(HttpStatusCode.OK, (await late.Client.PostAsync($"/api/group-invitations/{Assert.Single(mine!).Id}/accept", null)).StatusCode);
        }

        [Fact]
        public async Task LastLeader_CannotLeaveOrBeDemoted_UntilAnotherLeaderExists()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var other = await LoginAsync(factory, "other");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, other, group.Id, "Member");

            Assert.Equal(HttpStatusCode.BadRequest, (await leader.Client.PostAsync($"/api/groups/{group.Id}/leave", null)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await leader.Client.PutAsJsonAsync($"/api/groups/{group.Id}/members/{leader.UserId}", new GroupMemberRoleRequestDto { Role = "Member" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await leader.Client.DeleteAsync($"/api/groups/{group.Id}/members/{leader.UserId}")).StatusCode);

            var promote = await leader.Client.PutAsJsonAsync($"/api/groups/{group.Id}/members/{other.UserId}", new GroupMemberRoleRequestDto { Role = "Leader" });
            Assert.Equal(HttpStatusCode.OK, promote.StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await leader.Client.PostAsync($"/api/groups/{group.Id}/leave", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await leader.Client.GetAsync($"/api/groups/{group.Id}")).StatusCode);
        }

        [Fact]
        public async Task InvalidRole_Returns400()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var member = await LoginAsync(factory, "member");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, member, group.Id, "Member");

            var response = await leader.Client.PutAsJsonAsync($"/api/groups/{group.Id}/members/{member.UserId}", new GroupMemberRoleRequestDto { Role = "Boss" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // ================================================================ Giao việc

        [Fact]
        public async Task Assign_OnlyToLeaderOrMember_NotToOutsiderNorViewer()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var member = await LoginAsync(factory, "member");
            var viewer = await LoginAsync(factory, "viewer");
            var outsider = await LoginAsync(factory, "outsider");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, member, group.Id, "Member");
            await JoinAsync(leader, viewer, group.Id, "Viewer");
            var task = await CreateTaskAsync(leader, group.Id);

            var toOutsider = await leader.Client.PutAsJsonAsync($"/api/group-tasks/{task.Id}/assignee", new GroupAssigneeRequestDto { AssigneeId = outsider.UserId });
            var toViewer = await leader.Client.PutAsJsonAsync($"/api/group-tasks/{task.Id}/assignee", new GroupAssigneeRequestDto { AssigneeId = viewer.UserId });
            var onCreate = await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/tasks", new GroupTaskRequestDto { Title = "x", AssigneeId = outsider.UserId });
            Assert.Equal(HttpStatusCode.BadRequest, toOutsider.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, toViewer.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, onCreate.StatusCode);

            var toMember = await leader.Client.PutAsJsonAsync($"/api/group-tasks/{task.Id}/assignee", new GroupAssigneeRequestDto { AssigneeId = member.UserId });
            var assigned = (await toMember.Content.ReadFromJsonAsync<GroupTaskDto>())!;
            Assert.Equal(member.UserId, assigned.AssigneeId);
            Assert.Equal("Test member", assigned.AssigneeName);

            // Member không tự giao việc cho người khác
            Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.PutAsJsonAsync($"/api/group-tasks/{task.Id}/assignee", new GroupAssigneeRequestDto { AssigneeId = leader.UserId })).StatusCode);

            // "Việc của tôi" thấy việc vừa được giao
            var mine = (await member.Client.GetFromJsonAsync<MyGroupWorkDto>("/api/my-group-tasks"))!;
            Assert.Contains(mine.Tasks, t => t.TaskId == task.Id && t.GroupName == "Nhóm đồ án");
        }

        [Fact]
        public async Task RemovingMember_ClearsTheirAssignments_AndDemotingToViewerToo()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var member = await LoginAsync(factory, "member");
            var other = await LoginAsync(factory, "other");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, member, group.Id, "Member");
            await JoinAsync(leader, other, group.Id, "Member");
            var task = await CreateTaskAsync(leader, group.Id, assigneeId: member.UserId);
            var sub = await CreateSubTaskAsync(leader, task.Id, "Phần 1", member.UserId);
            var task2 = await CreateTaskAsync(leader, group.Id, "Việc khác", other.UserId);

            Assert.Equal(HttpStatusCode.NoContent, (await leader.Client.DeleteAsync($"/api/groups/{group.Id}/members/{member.UserId}")).StatusCode);

            Assert.Null((await GetTaskAsync(leader, task.Id)).AssigneeId);
            Assert.Null((await leader.Client.GetFromJsonAsync<GroupSubTaskDto>($"/api/group-subtasks/{sub.Id}"))!.AssigneeId);

            // Hạ xuống Viewer cũng bỏ giao việc
            var demote = await leader.Client.PutAsJsonAsync($"/api/groups/{group.Id}/members/{other.UserId}", new GroupMemberRoleRequestDto { Role = "Viewer" });
            Assert.Equal(HttpStatusCode.OK, demote.StatusCode);
            Assert.Null((await GetTaskAsync(leader, task2.Id)).AssigneeId);
        }

        // ================================================================ Status

        [Fact]
        public async Task Member_ChangesOnlyOwnTaskStatus_WithoutSubTasks()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var member = await LoginAsync(factory, "member");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, member, group.Id, "Member");
            var mine = await CreateTaskAsync(leader, group.Id, "Của tôi", member.UserId);
            var notMine = await CreateTaskAsync(leader, group.Id, "Của người khác", leader.UserId);
            var withSubs = await CreateTaskAsync(leader, group.Id, "Có việc con", member.UserId);
            await CreateSubTaskAsync(leader, withSubs.Id, "con 1", member.UserId);

            var ok = await SetTaskStatusAsync(member, mine.Id, "InProgress");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("InProgress", (await ok.Content.ReadFromJsonAsync<GroupTaskDto>())!.Status);

            var done = await SetTaskStatusAsync(member, mine.Id, "Done");
            var doneTask = (await done.Content.ReadFromJsonAsync<GroupTaskDto>())!;
            Assert.Equal("Done", doneTask.Status);
            Assert.NotNull(doneTask.CompletedAt);

            Assert.Equal(HttpStatusCode.Forbidden, (await SetTaskStatusAsync(member, notMine.Id, "InProgress")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SetTaskStatusAsync(member, withSubs.Id, "InProgress")).StatusCode);

            // Chuyển sai luật (Skipped không về Done trực tiếp) và giá trị lạ
            var skipped = await SetTaskStatusAsync(leader, notMine.Id, "Skipped");
            Assert.Equal(HttpStatusCode.OK, skipped.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SetTaskStatusAsync(leader, notMine.Id, "Done")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SetTaskStatusAsync(leader, notMine.Id, "Scheduled")).StatusCode);
        }

        [Fact]
        public async Task SubTaskStatus_DerivesTask_StartsDoneAndReopens()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var b = await LoginAsync(factory, "b");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            await JoinAsync(leader, b, group.Id, "Member");
            var task = await CreateTaskAsync(leader, group.Id);
            var s1 = await CreateSubTaskAsync(leader, task.Id, "Phần của A", a.UserId);
            var s2 = await CreateSubTaskAsync(leader, task.Id, "Phần của B", b.UserId);
            Assert.Equal("ToDo", (await GetTaskAsync(leader, task.Id)).Status);

            // A bắt đầu phần của mình: việc InProgress. A không đổi được phần của B
            Assert.Equal(HttpStatusCode.OK, (await SetSubTaskStatusAsync(a, s1.Id, "InProgress")).StatusCode);
            Assert.Equal("InProgress", (await GetTaskAsync(leader, task.Id)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SetSubTaskStatusAsync(a, s2.Id, "Done")).StatusCode);

            // Xong một phần: vẫn InProgress. Xong hết: việc tự Done
            var doneS1 = await SetSubTaskStatusAsync(a, s1.Id, "Done");
            var doneDto = (await doneS1.Content.ReadFromJsonAsync<GroupSubTaskDto>())!;
            Assert.Equal(a.UserId, doneDto.CompletedById);
            Assert.Equal("InProgress", (await GetTaskAsync(leader, task.Id)).Status);

            await SetSubTaskStatusAsync(b, s2.Id, "Done");
            var finished = await GetTaskAsync(leader, task.Id);
            Assert.Equal("Done", finished.Status);
            Assert.NotNull(finished.CompletedAt);
            Assert.Equal(2, finished.SubTaskDoneCount);

            // Thêm việc con mới thì việc đã Done được mở lại (còn phần đã Done nên là InProgress)
            await CreateSubTaskAsync(leader, task.Id, "Phần bổ sung", a.UserId);
            var reopened = await GetTaskAsync(leader, task.Id);
            Assert.Equal("InProgress", reopened.Status);
            Assert.Null(reopened.CompletedAt);
        }

        [Fact]
        public async Task ClosingTaskByHand_SkipsOpenSubTasks_AndSkippedTaskIsNotAutoChanged()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var group = await CreateGroupAsync(leader);
            var task = await CreateTaskAsync(leader, group.Id);
            var open = await CreateSubTaskAsync(leader, task.Id, "Chưa làm", leader.UserId);
            var progress = await CreateSubTaskAsync(leader, task.Id, "Đang làm", leader.UserId);
            await SetSubTaskStatusAsync(leader, progress.Id, "InProgress");

            Assert.Equal(HttpStatusCode.OK, (await SetTaskStatusAsync(leader, task.Id, "Done")).StatusCode);

            var subs = (await leader.Client.GetFromJsonAsync<List<GroupSubTaskDto>>($"/api/group-tasks/{task.Id}/subtasks"))!;
            Assert.All(subs, s => Assert.Equal("Skipped", s.Status));

            // Việc Skipped thì thêm việc con mới cũng không tự đổi
            var skipTask = await CreateTaskAsync(leader, group.Id, "Bỏ qua");
            await SetTaskStatusAsync(leader, skipTask.Id, "Skipped");
            await CreateSubTaskAsync(leader, skipTask.Id, "con");
            Assert.Equal("Skipped", (await GetTaskAsync(leader, skipTask.Id)).Status);
            _ = open;
        }

        [Fact]
        public async Task DeletingLastOpenSubTask_CanCompleteTheTask()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var group = await CreateGroupAsync(leader);
            var task = await CreateTaskAsync(leader, group.Id);
            var s1 = await CreateSubTaskAsync(leader, task.Id, "một");
            var s2 = await CreateSubTaskAsync(leader, task.Id, "hai");
            await SetSubTaskStatusAsync(leader, s1.Id, "Done");
            Assert.Equal("InProgress", (await GetTaskAsync(leader, task.Id)).Status);

            Assert.Equal(HttpStatusCode.NoContent, (await leader.Client.DeleteAsync($"/api/group-subtasks/{s2.Id}")).StatusCode);

            Assert.Equal("Done", (await GetTaskAsync(leader, task.Id)).Status);
        }

        [Fact]
        public async Task SubTask_DuplicateOrderIndex_Returns400_AndOrderDefaultsToEnd()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var group = await CreateGroupAsync(leader);
            var task = await CreateTaskAsync(leader, group.Id);
            var first = await CreateSubTaskAsync(leader, task.Id, "một");
            var second = await CreateSubTaskAsync(leader, task.Id, "hai");

            Assert.Equal(1, first.OrderIndex);
            Assert.Equal(2, second.OrderIndex);

            var duplicate = await leader.Client.PostAsJsonAsync($"/api/group-tasks/{task.Id}/subtasks",
                new GroupSubTaskRequestDto { Title = "trùng", OrderIndex = 1 });
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        }

        // ================================================================ Giai đoạn

        [Fact]
        public async Task Milestone_DeliveredWithOpenTasks_Returns409_UntilConfirmed()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var group = await CreateGroupAsync(leader);
            var milestone = await (await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/milestones",
                new GroupMilestoneRequestDto { Name = "Giai đoạn 1", OrderIndex = 1 })).Content.ReadFromJsonAsync<GroupMilestoneDto>();
            var task = await CreateTaskAsync(leader, group.Id, milestoneId: milestone!.Id);

            // Việc InProgress đẩy giai đoạn Planned lên InProgress
            await SetTaskStatusAsync(leader, task.Id, "InProgress");
            var afterStart = await leader.Client.GetFromJsonAsync<GroupMilestoneDto>($"/api/group-milestones/{milestone.Id}");
            Assert.Equal("InProgress", afterStart!.Status);

            var deliver = new GroupMilestoneRequestDto { Name = "Giai đoạn 1", OrderIndex = 1, Status = "Delivered" };
            var blocked = await leader.Client.PutAsJsonAsync($"/api/group-milestones/{milestone.Id}", deliver);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Contains("MILESTONE_HAS_OPEN_TASKS", await blocked.Content.ReadAsStringAsync());

            deliver.ConfirmOpenTasks = true;
            var confirmed = await leader.Client.PutAsJsonAsync($"/api/group-milestones/{milestone.Id}", deliver);
            var dto = (await confirmed.Content.ReadFromJsonAsync<GroupMilestoneDto>())!;
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
            Assert.Equal("Delivered", dto.Status);
            Assert.NotNull(dto.DeliveredAt);
        }

        [Fact]
        public async Task Milestone_RejectsDuplicateOrder_BadTransition_AndTasksInDeliveredMilestone()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var group = await CreateGroupAsync(leader);
            var other = await CreateGroupAsync(leader, "Nhóm khác");
            var m1 = await (await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/milestones",
                new GroupMilestoneRequestDto { Name = "M1", OrderIndex = 1 })).Content.ReadFromJsonAsync<GroupMilestoneDto>();
            var foreign = await (await leader.Client.PostAsJsonAsync($"/api/groups/{other.Id}/milestones",
                new GroupMilestoneRequestDto { Name = "Của nhóm khác", OrderIndex = 1 })).Content.ReadFromJsonAsync<GroupMilestoneDto>();

            Assert.Equal(HttpStatusCode.BadRequest, (await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/milestones",
                new GroupMilestoneRequestDto { Name = "M1 trùng", OrderIndex = 1 })).StatusCode);

            // Planned không nhảy thẳng sang Approved
            Assert.Equal(HttpStatusCode.BadRequest, (await leader.Client.PutAsJsonAsync($"/api/group-milestones/{m1!.Id}",
                new GroupMilestoneRequestDto { Name = "M1", OrderIndex = 1, Status = "Approved" })).StatusCode);

            // Việc không được gắn giai đoạn của nhóm khác
            var wrongGroup = await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/tasks",
                new GroupTaskRequestDto { Title = "x", MilestoneId = foreign!.Id });
            Assert.Equal(HttpStatusCode.BadRequest, wrongGroup.StatusCode);

            // Giai đoạn đã Delivered không nhận thêm việc
            await leader.Client.PutAsJsonAsync($"/api/group-milestones/{m1.Id}", new GroupMilestoneRequestDto { Name = "M1", OrderIndex = 1, Status = "InProgress" });
            await leader.Client.PutAsJsonAsync($"/api/group-milestones/{m1.Id}", new GroupMilestoneRequestDto { Name = "M1", OrderIndex = 1, Status = "Delivered" });
            var late = await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/tasks", new GroupTaskRequestDto { Title = "muộn", MilestoneId = m1.Id });
            Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
        }

        // ================================================================ Tiến độ, nhật ký, bình luận

        [Fact]
        public async Task Progress_CountsStatusesPercentAndMembers()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var member = await LoginAsync(factory, "member");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, member, group.Id, "Member");

            var empty = (await member.Client.GetFromJsonAsync<GroupProgressDto>($"/api/groups/{group.Id}/progress"))!;
            Assert.Equal(0, empty.TotalTasks);
            Assert.Equal(0, empty.CompletedPercent);

            var t1 = await CreateTaskAsync(leader, group.Id, "Một", member.UserId);
            var t2 = await CreateTaskAsync(leader, group.Id, "Hai", member.UserId);
            var t3 = await CreateTaskAsync(leader, group.Id, "Ba");
            var t4 = await CreateTaskAsync(leader, group.Id, "Bốn");
            await SetTaskStatusAsync(member, t1.Id, "Done");
            await SetTaskStatusAsync(member, t2.Id, "InProgress");
            await SetTaskStatusAsync(leader, t4.Id, "Skipped");
            _ = t3;

            var progress = (await leader.Client.GetFromJsonAsync<GroupProgressDto>($"/api/groups/{group.Id}/progress"))!;

            Assert.Equal(4, progress.TotalTasks);
            Assert.Equal(1, progress.TasksByStatus["Done"]);
            Assert.Equal(1, progress.TasksByStatus["InProgress"]);
            Assert.Equal(1, progress.TasksByStatus["ToDo"]);
            Assert.Equal(1, progress.TasksByStatus["Skipped"]);
            Assert.Equal(2, progress.RemainingTasks);
            Assert.Equal(1, progress.UnassignedTasks);
            Assert.Equal(33.3, progress.CompletedPercent);

            var memberRow = Assert.Single(progress.Members, m => m.UserId == member.UserId);
            Assert.Equal(2, memberRow.AssignedTasks);
            Assert.Equal(1, memberRow.DoneTasks);
        }

        [Fact]
        public async Task Activity_RecordsWhoDidWhat_NewestFirst()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var member = await LoginAsync(factory, "member");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, member, group.Id, "Member");
            var task = await CreateTaskAsync(leader, group.Id, "Việc A", member.UserId);
            await SetTaskStatusAsync(member, task.Id, "InProgress");

            var log = (await member.Client.GetFromJsonAsync<List<GroupActivityDto>>($"/api/groups/{group.Id}/activity"))!;

            Assert.Equal("StatusChanged", log[0].Action);
            Assert.Equal(member.UserId, log[0].ActorId);
            Assert.Equal("ToDo", log[0].OldValue);
            Assert.Equal("InProgress", log[0].NewValue);
            Assert.Contains(log, l => l.EntityType == "Task" && l.Action == "Created" && l.ActorId == leader.UserId);
            Assert.Contains(log, l => l.EntityType == "Member" && l.Action == "MemberJoined");
            Assert.Contains(log, l => l.EntityType == "Group" && l.Action == "Created");
        }

        [Fact]
        public async Task Comments_AuthorEditsLeaderDeletes_OthersForbidden()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var a = await LoginAsync(factory, "a");
            var b = await LoginAsync(factory, "b");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, a, group.Id, "Member");
            await JoinAsync(leader, b, group.Id, "Member");
            var task = await CreateTaskAsync(leader, group.Id);

            var created = await a.Client.PostAsJsonAsync($"/api/group-tasks/{task.Id}/comments", new GroupCommentRequestDto { Content = "  lần đầu  " });
            var comment = (await created.Content.ReadFromJsonAsync<GroupCommentDto>())!;
            Assert.Equal("lần đầu", comment.Content);
            Assert.Equal("Test a", comment.AuthorName);

            Assert.Equal(HttpStatusCode.BadRequest, (await a.Client.PostAsJsonAsync($"/api/group-tasks/{task.Id}/comments", new GroupCommentRequestDto { Content = " " })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Client.PutAsJsonAsync($"/api/group-comments/{comment.Id}", new GroupCommentRequestDto { Content = "sửa trộm" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Client.DeleteAsync($"/api/group-comments/{comment.Id}")).StatusCode);

            var edited = await a.Client.PutAsJsonAsync($"/api/group-comments/{comment.Id}", new GroupCommentRequestDto { Content = "đã sửa" });
            var editedDto = (await edited.Content.ReadFromJsonAsync<GroupCommentDto>())!;
            Assert.Equal("đã sửa", editedDto.Content);
            Assert.NotNull(editedDto.EditedAt);

            Assert.Equal(HttpStatusCode.NoContent, (await leader.Client.DeleteAsync($"/api/group-comments/{comment.Id}")).StatusCode);
            Assert.Empty((await b.Client.GetFromJsonAsync<List<GroupCommentDto>>($"/api/group-tasks/{task.Id}/comments"))!);
        }

        // ================================================================ Lưu trữ

        [Fact]
        public async Task ArchivedGroup_IsReadOnly_UntilRestored()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var group = await CreateGroupAsync(leader);
            var task = await CreateTaskAsync(leader, group.Id);

            var archived = await leader.Client.PostAsync($"/api/groups/{group.Id}/archive", null);
            Assert.Equal("Archived", (await archived.Content.ReadFromJsonAsync<GroupDto>())!.Status);

            Assert.Equal(HttpStatusCode.BadRequest, (await leader.Client.PostAsJsonAsync($"/api/groups/{group.Id}/tasks", new GroupTaskRequestDto { Title = "mới" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SetTaskStatusAsync(leader, task.Id, "InProgress")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await leader.Client.GetAsync($"/api/group-tasks/{task.Id}")).StatusCode);
            Assert.DoesNotContain((await leader.Client.GetFromJsonAsync<List<GroupDto>>("/api/groups"))!, g => g.Id == group.Id);
            Assert.Contains((await leader.Client.GetFromJsonAsync<List<GroupDto>>("/api/groups?includeArchived=true"))!, g => g.Id == group.Id);

            Assert.Equal(HttpStatusCode.OK, (await leader.Client.PostAsync($"/api/groups/{group.Id}/restore", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SetTaskStatusAsync(leader, task.Id, "InProgress")).StatusCode);
        }

        // ================================================================ Thông báo (dùng chung bảng Notification)

        [Fact]
        public async Task Notifications_AssignedStatusChangedAndMention_GoToTheRightPeople()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var member = await LoginAsync(factory, "member");
            var group = await CreateGroupAsync(leader);

            // Mời: người được mời đã có tài khoản nên nhận thông báo GroupInvited
            await JoinAsync(leader, member, group.Id, "Member");
            var task = await CreateTaskAsync(leader, group.Id, "Việc A", member.UserId);

            // Leader đổi status việc của member: member được báo. Member tự đổi thì không tự báo mình
            await SetTaskStatusAsync(leader, task.Id, "InProgress");
            await SetTaskStatusAsync(member, task.Id, "Done");
            await leader.Client.PostAsJsonAsync($"/api/group-tasks/{task.Id}/comments", new GroupCommentRequestDto { Content = $"Nhờ @{member.Email} xem lại" });

            var inbox = (await member.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!;
            var types = inbox.Select(n => n.Type).ToList();
            Assert.Contains("GroupInvited", types);
            Assert.Contains("GroupAssigned", types);
            Assert.Equal(1, types.Count(t => t == "GroupStatusChanged"));
            Assert.Contains("GroupMention", types);

            // Mỗi thông báo mang liên kết để FE mở thẳng: lời mời chỉ có nhóm, các loại còn lại có cả việc
            var invited = inbox.Single(n => n.Type == "GroupInvited");
            Assert.Equal(group.Id, invited.GroupId);
            Assert.Null(invited.GroupTaskId);
            foreach (var n in inbox.Where(n => n.Type is "GroupAssigned" or "GroupStatusChanged" or "GroupMention"))
            {
                Assert.Equal(group.Id, n.GroupId);
                Assert.Equal(task.Id, n.GroupTaskId);
            }

            // Leader tự thao tác thì không nhận thông báo về chính mình
            var leaderInbox = (await leader.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!;
            Assert.DoesNotContain(leaderInbox, n => n.Type is "GroupAssigned" or "GroupStatusChanged");
        }

        [Fact]
        public async Task Notifications_ForSubTaskAssignment_LinkToTheParentTask()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var member = await LoginAsync(factory, "member");
            var group = await CreateGroupAsync(leader);
            await JoinAsync(leader, member, group.Id, "Member");
            var task = await CreateTaskAsync(leader, group.Id, "Việc cha");

            await CreateSubTaskAsync(leader, task.Id, "Việc con", member.UserId);

            var inbox = (await member.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!;
            var assigned = inbox.Single(n => n.Type == "GroupAssigned");
            Assert.Equal(group.Id, assigned.GroupId);
            Assert.Equal(task.Id, assigned.GroupTaskId);
            Assert.Null(assigned.ScheduleSlotId);
        }

        // ================================================================ Việc của tôi

        [Fact]
        public async Task MyGroupTasks_ListsAssignedTasksAndSubTasks_AcrossGroups_ExcludingArchived()
        {
            using var factory = new ApiFactory();
            var leader = await LoginAsync(factory, "leader");
            var member = await LoginAsync(factory, "member");
            var g1 = await CreateGroupAsync(leader, "Nhóm 1");
            var g2 = await CreateGroupAsync(leader, "Nhóm 2");
            await JoinAsync(leader, member, g1.Id, "Member");
            await JoinAsync(leader, member, g2.Id, "Member");
            var t1 = await CreateTaskAsync(leader, g1.Id, "Việc nhóm 1", member.UserId);
            var t2 = await CreateTaskAsync(leader, g2.Id, "Việc nhóm 2");
            await CreateSubTaskAsync(leader, t2.Id, "Việc con nhóm 2", member.UserId);

            var mine = (await member.Client.GetFromJsonAsync<MyGroupWorkDto>("/api/my-group-tasks"))!;
            Assert.Single(mine.Tasks, t => t.TaskId == t1.Id);
            Assert.Single(mine.SubTasks, s => s.GroupName == "Nhóm 2" && s.TaskTitle == "Việc nhóm 2");

            await leader.Client.PostAsync($"/api/groups/{g1.Id}/archive", null);
            var afterArchive = (await member.Client.GetFromJsonAsync<MyGroupWorkDto>("/api/my-group-tasks"))!;
            Assert.Empty(afterArchive.Tasks);
            Assert.Single(afterArchive.SubTasks);
        }

        // ================================================================ Nhánh cá nhân không bị ảnh hưởng

        [Fact]
        public async Task PersonalBranch_StillWorks_AlongsideGroups()
        {
            using var factory = new ApiFactory();
            var user = await LoginAsync(factory, "solo");
            var group = await CreateGroupAsync(user);

            var client = await user.Client.PostAsJsonAsync("/api/clients", new ClientRequestDto { Name = "Acme" });
            var created = await user.Client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Dự án riêng" });
            var projects = await user.Client.GetFromJsonAsync<List<ProjectWithClientDto>>("/api/projects");

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal(HttpStatusCode.Created, client.StatusCode);
            Assert.NotEmpty(projects!);
            Assert.NotEqual(Guid.Empty, group.Id);
        }
    }
}
