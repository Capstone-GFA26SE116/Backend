using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Test.Infrastructure;
using Xunit;
using DomainEntities = METANOIA.Domain.Entities;

namespace METANOIA.Test
{
    public class TaskTests
    {
        private static readonly Guid DomainId = Guid.NewGuid();

        private static TaskRequestDto FlexibleTask(string title = "Viết báo cáo", string status = "ToDo") => new()
        {
            Title = title,
            Status = status,
            TaskType = "Flexible",
            DomainId = DomainId,
            Deadline = new DateTimeOffset(2026, 10, 20, 17, 0, 0, TimeSpan.FromHours(7)),
            EstimatedMinutes = 120
        };

        private static async Task<(HttpClient Client, Guid ProjectId)> SetupAsync(ApiFactory factory, string subject = "user-1")
        {
            await factory.SeedAsync(db => db.Domains.Add(new DomainEntities.Domain
            {
                Id = DomainId,
                Name = $"Dev-{Guid.NewGuid()}",
                IsActive = true
            }));

            var (client, _) = await TestHelpers.LoginAsync(factory, subject);
            var project = await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Website" });
            return (client, (await project.Content.ReadFromJsonAsync<ProjectDto>())!.Id);
        }

        [Fact]
        public async Task CreateFlexibleTask_ThenGet_ReturnsUtcDeadline()
        {
            using var factory = new ApiFactory();
            var (client, projectId) = await SetupAsync(factory);

            var created = await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks", FlexibleTask());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var dto = (await created.Content.ReadFromJsonAsync<TaskDto>())!;

            var fetched = await client.GetFromJsonAsync<TaskDto>($"/api/tasks/{dto.Id}");
            Assert.Equal("ToDo", fetched!.Status);
            Assert.Equal(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero), fetched.Deadline);
        }

        [Fact]
        public async Task FlexibleTaskWithoutDeadline_Returns400()
        {
            using var factory = new ApiFactory();
            var (client, projectId) = await SetupAsync(factory);
            var request = FlexibleTask();
            request.Deadline = null;

            var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task FixedTaskWithEndBeforeStart_Returns400()
        {
            using var factory = new ApiFactory();
            var (client, projectId) = await SetupAsync(factory);
            var request = new TaskRequestDto
            {
                Title = "Họp khách",
                TaskType = "Fixed",
                DomainId = DomainId,
                FixedStart = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero),
                FixedEnd = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero)
            };

            var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task MarkingDone_SetsCompletedAt_AndReopening_ClearsIt()
        {
            using var factory = new ApiFactory();
            var (client, projectId) = await SetupAsync(factory);
            var created = await (await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks", FlexibleTask()))
                .Content.ReadFromJsonAsync<TaskDto>();

            var done = await (await client.PutAsJsonAsync($"/api/tasks/{created!.Id}", FlexibleTask(status: "Done")))
                .Content.ReadFromJsonAsync<TaskDto>();
            Assert.NotNull(done!.CompletedAt);

            var reopened = await (await client.PutAsJsonAsync($"/api/tasks/{created.Id}", FlexibleTask(status: "InProgress")))
                .Content.ReadFromJsonAsync<TaskDto>();
            Assert.Null(reopened!.CompletedAt);
        }

        [Fact]
        public async Task ListByStatus_ReturnsOnlyMatchingTasks()
        {
            using var factory = new ApiFactory();
            var (client, projectId) = await SetupAsync(factory);
            await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks", FlexibleTask("A"));
            await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks", FlexibleTask("B", status: "Done"));

            var done = await client.GetFromJsonAsync<List<TaskDto>>($"/api/projects/{projectId}/tasks?status=Done");

            Assert.Single(done!);
            Assert.Equal("B", done![0].Title);
        }

        [Fact]
        public async Task DeleteTask_WithFocusSession_Returns400()
        {
            using var factory = new ApiFactory();
            var (client, projectId) = await SetupAsync(factory);
            var created = await (await client.PostAsJsonAsync($"/api/projects/{projectId}/tasks", FlexibleTask()))
                .Content.ReadFromJsonAsync<TaskDto>();
            await factory.SeedAsync(db => db.FocusSessions.Add(new DomainEntities.FocusSession
            {
                Id = Guid.NewGuid(),
                TaskId = created!.Id,
                StartedAt = DateTime.UtcNow.AddHours(-1),
                EndedAt = DateTime.UtcNow,
                CaptureMethod = "Timer",
                IsConfirmed = true
            }));

            var response = await client.DeleteAsync($"/api/tasks/{created.Id}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task OtherUsersProject_Returns404OnCreate()
        {
            using var factory = new ApiFactory();
            var (owner, projectId) = await SetupAsync(factory, "owner");

            var (stranger, _) = await TestHelpers.LoginAsync(factory, "stranger");
            var response = await stranger.PostAsJsonAsync($"/api/projects/{projectId}/tasks", FlexibleTask());

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
