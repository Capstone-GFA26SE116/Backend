using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Test.Infrastructure;
using Xunit;
using DomainEntities = METANOIA.Domain.Entities;

namespace METANOIA.Test
{
    public class ScheduleTests
    {
        private static readonly Guid DomainId = Guid.NewGuid();

        private static async Task<(HttpClient Client, Guid TaskId)> SetupTaskAsync(ApiFactory factory)
        {
            await factory.SeedAsync(db => db.Domains.Add(new DomainEntities.Domain
            {
                Id = DomainId,
                Name = $"Dev-{Guid.NewGuid()}",
                IsActive = true
            }));

            var (client, _) = await TestHelpers.LoginAsync(factory);
            var project = await (await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Website" }))
                .Content.ReadFromJsonAsync<ProjectDto>();
            var task = await (await client.PostAsJsonAsync($"/api/projects/{project!.Id}/tasks", new TaskRequestDto
            {
                Title = "Viết báo cáo",
                TaskType = "Flexible",
                DomainId = DomainId,
                Deadline = new DateTimeOffset(2026, 10, 20, 17, 0, 0, TimeSpan.Zero),
                EstimatedMinutes = 120
            })).Content.ReadFromJsonAsync<TaskDto>();

            return (client, task!.Id);
        }

        private static ScheduleSlotRequestDto Slot(int startHour, int minutes)
        {
            var start = new DateTimeOffset(2026, 10, 10, startHour, 0, 0, TimeSpan.Zero);
            return new ScheduleSlotRequestDto
            {
                StartTime = start,
                EndTime = start.AddMinutes(minutes),
                IsManualOverride = true,
                Explanation = "Xếp theo deadline"
            };
        }

        [Fact]
        public async Task CreateSlot_ComputesProposedMinutes_AndLogsCreated()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupTaskAsync(factory);

            var created = await client.PostAsJsonAsync($"/api/tasks/{taskId}/schedule-slots", Slot(9, 90));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var slot = (await created.Content.ReadFromJsonAsync<ScheduleSlotDto>())!;

            Assert.Equal(90, slot.ProposedMinutes);
            Assert.Equal("Proposed", slot.Status);

            var changes = await client.GetFromJsonAsync<List<ScheduleChangeLogDto>>($"/api/schedule-slots/{slot.Id}/changes");
            Assert.Single(changes!);
            Assert.Equal("Created", changes![0].ChangeType);
        }

        [Fact]
        public async Task MovingSlot_LogsMoved_AndKeepsOriginalStart()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupTaskAsync(factory);
            var slot = await (await client.PostAsJsonAsync($"/api/tasks/{taskId}/schedule-slots", Slot(9, 60)))
                .Content.ReadFromJsonAsync<ScheduleSlotDto>();

            var moved = await (await client.PutAsJsonAsync($"/api/schedule-slots/{slot!.Id}", Slot(14, 60)))
                .Content.ReadFromJsonAsync<ScheduleSlotDto>();

            Assert.Equal(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero), moved!.OriginalStartTime);
            var changes = await client.GetFromJsonAsync<List<ScheduleChangeLogDto>>($"/api/schedule-slots/{slot.Id}/changes");
            Assert.Contains(changes!, c => c.ChangeType == "Moved");
        }

        [Fact]
        public async Task CancelSlot_SetsCancelled_AndLogsDeleted()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupTaskAsync(factory);
            var slot = await (await client.PostAsJsonAsync($"/api/tasks/{taskId}/schedule-slots", Slot(9, 60)))
                .Content.ReadFromJsonAsync<ScheduleSlotDto>();

            var response = await client.DeleteAsync($"/api/schedule-slots/{slot!.Id}");
            var cancelled = await response.Content.ReadFromJsonAsync<ScheduleSlotDto>();

            Assert.Equal("Cancelled", cancelled!.Status);
            var changes = await client.GetFromJsonAsync<List<ScheduleChangeLogDto>>($"/api/schedule-slots/{slot.Id}/changes");
            Assert.Contains(changes!, c => c.ChangeType == "Deleted");
        }

        [Fact]
        public async Task SlotWithEndBeforeStart_Returns400()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupTaskAsync(factory);

            var response = await client.PostAsJsonAsync($"/api/tasks/{taskId}/schedule-slots", Slot(9, -30));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task ManualChangeLog_WithInvalidChangeType_Returns400()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupTaskAsync(factory);
            var slot = await (await client.PostAsJsonAsync($"/api/tasks/{taskId}/schedule-slots", Slot(9, 60)))
                .Content.ReadFromJsonAsync<ScheduleSlotDto>();

            var response = await client.PostAsJsonAsync($"/api/schedule-slots/{slot!.Id}/changes",
                new ScheduleChangeLogRequestDto { ChangeType = "Edited", Source = "Manual" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task SubTask_DuplicateOrderIndex_Returns400()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupTaskAsync(factory);
            var first = new SubTaskRequestDto { Title = "Phác thảo", EstimatedMinutes = 30, OrderIndex = 1 };
            await client.PostAsJsonAsync($"/api/tasks/{taskId}/subtasks", first);

            var response = await client.PostAsJsonAsync($"/api/tasks/{taskId}/subtasks", first);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task SubTask_WithFocusHistory_CannotBeDeleted()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupTaskAsync(factory);
            var subTask = await (await client.PostAsJsonAsync($"/api/tasks/{taskId}/subtasks",
                new SubTaskRequestDto { Title = "Viết", EstimatedMinutes = 30, OrderIndex = 1 }))
                .Content.ReadFromJsonAsync<SubTaskDto>();
            await factory.SeedAsync(db => db.FocusSessions.Add(new DomainEntities.FocusSession
            {
                Id = Guid.NewGuid(),
                TaskId = taskId,
                SubTaskId = subTask!.Id,
                StartedAt = DateTime.UtcNow.AddHours(-1),
                EndedAt = DateTime.UtcNow,
                CaptureMethod = "Timer",
                IsConfirmed = true
            }));

            var response = await client.DeleteAsync($"/api/subtasks/{subTask.Id}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
