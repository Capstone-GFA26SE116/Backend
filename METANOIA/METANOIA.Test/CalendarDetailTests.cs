using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Domain.Entities;
using METANOIA.Test.Infrastructure;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // GET /api/calendar trả đủ thông tin của slot (subtask, dự án, khách hàng, loại việc, task) và khung giờ làm việc của user
    public class CalendarDetailTests
    {
        private static readonly Guid DomainId = Guid.NewGuid();
        private const string Day = "from=2026-10-10T00:00:00Z&to=2026-10-11T00:00:00Z";

        private sealed record Setup(HttpClient Client, Guid UserId, Guid ProjectId, Guid TaskId);

        private static async Task<Setup> SetupAsync(ApiFactory factory, string subject = "user-1", Guid? clientId = null)
        {
            await factory.SeedAsync(db =>
            {
                if (!db.Domains.Any(d => d.Id == DomainId))
                {
                    db.Domains.Add(new METANOIA.Domain.Entities.Domain { Id = DomainId, Name = "Viết lách", IsActive = true });
                }
            });

            var (client, auth) = await TestHelpers.LoginAsync(factory, subject);
            var project = await (await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Website bán hàng", ClientId = clientId }))
                .Content.ReadFromJsonAsync<ProjectDto>();
            var task = await (await client.PostAsJsonAsync($"/api/projects/{project!.Id}/tasks", new TaskRequestDto
            {
                Title = "Viết báo cáo",
                TaskType = "Flexible",
                DomainId = DomainId,
                Deadline = new DateTimeOffset(2026, 10, 20, 17, 0, 0, TimeSpan.Zero),
                EstimatedMinutes = 120
            })).Content.ReadFromJsonAsync<TaskDto>();
            return new Setup(client, auth.UserId, project.Id, task!.Id);
        }

        private static async Task<ScheduleSlotDto> CreateSlotAsync(
            HttpClient client, Guid taskId, int hour, int minutes = 60, Guid? subTaskId = null, string? explanation = null, bool manual = false)
        {
            var start = new DateTimeOffset(2026, 10, 10, hour, 0, 0, TimeSpan.Zero);
            var response = await client.PostAsJsonAsync($"/api/tasks/{taskId}/schedule-slots", new ScheduleSlotRequestDto
            {
                SubTaskId = subTaskId,
                StartTime = start,
                EndTime = start.AddMinutes(minutes),
                Explanation = explanation,
                IsManualOverride = manual
            });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ScheduleSlotDto>())!;
        }

        private static async Task<CalendarDto> GetAsync(HttpClient client, string extra = "")
        {
            return (await client.GetFromJsonAsync<CalendarDto>($"/api/calendar?{Day}{extra}"))!;
        }

        // ------------------------------------------------------------ Thông tin chi tiết của slot

        [Fact]
        public async Task Slot_CarriesSubTaskProjectClientDomainAndTaskDetails()
        {
            using var factory = new ApiFactory();
            await TestHelpers.LoginAsync(factory, "user-1");
            var client = (await (await (await TestHelpers.LoginAsync(factory, "user-1")).Client.PostAsJsonAsync("/api/clients", new ClientRequestDto { Name = "Công ty Acme" }))
                .Content.ReadFromJsonAsync<ClientDto>())!;
            var s = await SetupAsync(factory, clientId: client.Id);
            var subTask = await (await s.Client.PostAsJsonAsync($"/api/tasks/{s.TaskId}/subtasks", new SubTaskRequestDto
            {
                Title = "Phần mở đầu",
                EstimatedMinutes = 30,
                OrderIndex = 1
            })).Content.ReadFromJsonAsync<SubTaskDto>();
            var slot = await CreateSlotAsync(s.Client, s.TaskId, hour: 9, minutes: 90, subTaskId: subTask!.Id,
                explanation: "Xếp trước deadline", manual: true);

            var calendar = await GetAsync(s.Client);

            var item = Assert.Single(calendar.Items);
            Assert.Equal("Slot", item.Source);
            Assert.Equal(slot.Id.ToString(), item.Id);
            Assert.Equal("Viết báo cáo", item.Title);
            Assert.Equal(s.TaskId, item.TaskId);
            Assert.Equal(subTask.Id, item.SubTaskId);
            Assert.Equal("Phần mở đầu", item.SubTaskTitle);
            Assert.Equal(s.ProjectId, item.ProjectId);
            Assert.Equal("Website bán hàng", item.ProjectName);
            Assert.Equal("Công ty Acme", item.ClientName);
            Assert.Equal("Viết lách", item.DomainName);
            Assert.Equal("Flexible", item.TaskType);
            Assert.Equal("ToDo", item.TaskStatus);
            Assert.Equal(new DateTimeOffset(2026, 10, 20, 17, 0, 0, TimeSpan.Zero), item.TaskDeadline);
            Assert.Equal(90, item.ProposedMinutes);
            Assert.Equal("Xếp trước deadline", item.Explanation);
            Assert.True(item.IsManualOverride);
            Assert.False(item.SyncFailed);
            Assert.Equal("Proposed", item.Status);
        }

        [Fact]
        public async Task Slot_WithoutSubTaskOrClient_HasNullsForThem()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            await CreateSlotAsync(s.Client, s.TaskId, hour: 9);

            var item = Assert.Single((await GetAsync(s.Client)).Items);

            Assert.Null(item.SubTaskId);
            Assert.Null(item.SubTaskTitle);
            Assert.Null(item.ClientName);
            Assert.Equal("Website bán hàng", item.ProjectName);
            Assert.Null(item.Explanation);
            Assert.False(item.IsManualOverride);
        }

        [Fact]
        public async Task Slot_ReportsSyncFailed_AndTheCurrentTaskStatus()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var slot = await CreateSlotAsync(s.Client, s.TaskId, hour: 9);
            await factory.SeedAsync(db =>
            {
                db.ScheduleSlots.Single(x => x.Id == slot.Id).SyncFailed = true;
                db.Tasks.Single(t => t.Id == s.TaskId).Status = "InProgress";
            });

            var item = Assert.Single((await GetAsync(s.Client)).Items);

            Assert.True(item.SyncFailed);
            Assert.Equal("InProgress", item.TaskStatus);
        }

        [Fact]
        public async Task ManySlots_AreAllReturnedWithTheirOwnDetails_InOneResponse()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var second = await (await s.Client.PostAsJsonAsync($"/api/projects/{s.ProjectId}/tasks", new TaskRequestDto
            {
                Title = "Task thứ hai",
                TaskType = "Flexible",
                DomainId = DomainId,
                Deadline = new DateTimeOffset(2026, 10, 25, 17, 0, 0, TimeSpan.Zero),
                EstimatedMinutes = 60
            })).Content.ReadFromJsonAsync<TaskDto>();
            for (var hour = 8; hour < 14; hour += 2)
            {
                await CreateSlotAsync(s.Client, hour % 4 == 0 ? s.TaskId : second!.Id, hour);
            }

            var items = (await GetAsync(s.Client)).Items;

            Assert.Equal(3, items.Count);
            Assert.All(items, i => Assert.Equal("Website bán hàng", i.ProjectName));
            Assert.All(items, i => Assert.Equal("Viết lách", i.DomainName));
            Assert.Equal(new[] { "Viết báo cáo", "Task thứ hai", "Viết báo cáo" }, items.Select(i => i.Title).ToArray());
        }

        // ------------------------------------------------------------ Khung giờ làm việc

        [Fact]
        public async Task Response_HasTheUsersWorkingHours_AtTheTopLevel()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");

            var calendar = await GetAsync(client);

            Assert.Equal("Asia/Ho_Chi_Minh", calendar.TimeZone);
            Assert.Equal(new TimeOnly(8, 0), calendar.WorkDayStart);
            Assert.Equal(new TimeOnly(18, 0), calendar.WorkDayEnd);
            Assert.False(calendar.WorksOnWeekend);
            Assert.Empty(calendar.Items);
        }

        [Fact]
        public async Task WorkingHours_FollowTheSettingsTheUserChanged()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");
            (await client.PutAsJsonAsync("/api/me/settings", new UpdateSettingsRequestDto
            {
                WorkDayStart = new TimeOnly(7, 30),
                WorkDayEnd = new TimeOnly(16, 0),
                WorksOnWeekend = true,
                DefaultBufferMinutes = 10,
                ReminderLeadMinutes = 15,
                TimeZone = "Asia/Bangkok"
            })).EnsureSuccessStatusCode();

            var calendar = await GetAsync(client);

            Assert.Equal("Asia/Bangkok", calendar.TimeZone);
            Assert.Equal(new TimeOnly(7, 30), calendar.WorkDayStart);
            Assert.Equal(new TimeOnly(16, 0), calendar.WorkDayEnd);
            Assert.True(calendar.WorksOnWeekend);
        }

        // ------------------------------------------------------------ Slot bị hủy và event Google

        [Fact]
        public async Task CancelledSlots_AreHiddenByDefault_AndShownOnRequest()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            await CreateSlotAsync(s.Client, s.TaskId, hour: 9);
            var cancelled = await CreateSlotAsync(s.Client, s.TaskId, hour: 11);
            await s.Client.DeleteAsync($"/api/schedule-slots/{cancelled.Id}");

            var normal = await GetAsync(s.Client);
            var all = await GetAsync(s.Client, "&includeCancelled=true");

            Assert.Single(normal.Items);
            Assert.DoesNotContain(normal.Items, i => i.Status == "Cancelled");
            Assert.Equal(2, all.Items.Count);
            var shown = all.Items.Single(i => i.Status == "Cancelled");
            Assert.Equal("Website bán hàng", shown.ProjectName); // slot bị hủy vẫn đủ thông tin
        }

        [Fact]
        public async Task GoogleSide_OnlyHasEventsThatAreNotCancelledAndNotCreatedByMetanoia_WithNullSlotFields()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var connectionId = Guid.NewGuid();
            await factory.SeedAsync(db =>
            {
                db.GoogleConnections.Add(new GoogleConnection
                {
                    Id = connectionId,
                    UserId = s.UserId,
                    AccessTokenEncrypted = new byte[] { 1 },
                    RefreshTokenEncrypted = new byte[] { 1 },
                    TokenExpiresAt = DateTime.UtcNow.AddHours(1),
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow
                });

                GoogleCalendarEvent Event(string id, string title, int hour, bool cancelled = false, bool byMetanoia = false) => new()
                {
                    Id = Guid.NewGuid(),
                    GoogleConnectionId = connectionId,
                    GoogleEventId = id,
                    Title = title,
                    StartTime = new DateTime(2026, 10, 10, hour, 0, 0, DateTimeKind.Utc),
                    EndTime = new DateTime(2026, 10, 10, hour, 30, 0, DateTimeKind.Utc),
                    Etag = "e",
                    IsCancelled = cancelled,
                    IsCreatedByMetanoia = byMetanoia,
                    UpdatedAt = DateTime.UtcNow
                };

                db.GoogleCalendarEvents.AddRange(
                    Event("keep", "Họp Google", 8),
                    Event("cancelled", "Đã hủy", 9, cancelled: true),
                    Event("ours", "Do METANOIA tạo", 10, byMetanoia: true),
                    Event("both", "Hủy và do METANOIA tạo", 11, cancelled: true, byMetanoia: true));
            });
            await CreateSlotAsync(s.Client, s.TaskId, hour: 14);

            var calendar = await GetAsync(s.Client);

            Assert.Equal("Active", calendar.GoogleCalendarStatus);
            var google = Assert.Single(calendar.Items, i => i.Source == "Google");
            Assert.Equal("Họp Google", google.Title);
            Assert.Null(google.Status);
            Assert.Null(google.TaskId);
            Assert.Null(google.ProjectName);
            Assert.Null(google.ClientName);
            Assert.Null(google.SubTaskTitle);
            Assert.Null(google.TaskType);
            Assert.Null(google.TaskDeadline);
            Assert.Null(google.ProposedMinutes);
            Assert.Null(google.IsManualOverride);
            Assert.Null(google.SyncFailed);
            Assert.Equal(2, calendar.Items.Count); // event Google + slot
        }

        [Fact]
        public async Task OtherUsersSlots_NeverAppear_AndSlotsAreSortedWithGoogleEvents()
        {
            using var factory = new ApiFactory();
            var mine = await SetupAsync(factory, "user-1");
            var theirs = await SetupAsync(factory, "user-2");
            await CreateSlotAsync(mine.Client, mine.TaskId, hour: 10);
            await CreateSlotAsync(theirs.Client, theirs.TaskId, hour: 9);

            var items = (await GetAsync(mine.Client)).Items;

            var only = Assert.Single(items);
            Assert.Equal(mine.TaskId, only.TaskId);
        }
    }
}
