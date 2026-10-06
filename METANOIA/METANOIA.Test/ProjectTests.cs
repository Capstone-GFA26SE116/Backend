using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Test.Infrastructure;
using Xunit;
using DomainEntities = METANOIA.Domain.Entities;

namespace METANOIA.Test
{
    public class ProjectTests
    {
        private static ProjectRequestDto NewProject(string name = "Website redesign") => new()
        {
            Name = name,
            Description = "Demo",
            StartDate = new DateOnly(2026, 10, 1),
            Deadline = new DateOnly(2026, 10, 31)
        };

        [Fact]
        public async Task CreateProject_ThenGet_ReturnsSameProject()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);

            var created = await client.PostAsJsonAsync("/api/projects", NewProject());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var project = (await created.Content.ReadFromJsonAsync<ProjectDto>())!;

            var fetched = await client.GetFromJsonAsync<ProjectDto>($"/api/projects/{project.Id}");
            Assert.Equal("Website redesign", fetched!.Name);
            Assert.Equal("Active", fetched.Status);
        }

        [Fact]
        public async Task CreateProject_WithEmptyName_Returns400()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);

            var response = await client.PostAsJsonAsync("/api/projects", NewProject(name: "   "));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task CreateProject_WithDeadlineBeforeStart_Returns400()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);
            var request = NewProject();
            request.Deadline = new DateOnly(2026, 9, 1);

            var response = await client.PostAsJsonAsync("/api/projects", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task ArchivedProject_HiddenByDefault_ButListedWhenIncluded()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);
            var project = await CreateAsync(client, NewProject());

            await client.PostAsync($"/api/projects/{project.Id}/archive", null);

            var active = await client.GetFromJsonAsync<List<ProjectDto>>("/api/projects");
            var all = await client.GetFromJsonAsync<List<ProjectDto>>("/api/projects?includeArchived=true");
            Assert.DoesNotContain(active!, p => p.Id == project.Id);
            Assert.Contains(all!, p => p.Id == project.Id && p.Status == "Archived");
        }

        [Fact]
        public async Task OtherUsersProject_Returns404()
        {
            using var factory = new ApiFactory();
            var (owner, _) = await TestHelpers.LoginAsync(factory, "owner");
            var project = await CreateAsync(owner, NewProject());

            var (stranger, _) = await TestHelpers.LoginAsync(factory, "stranger");
            var response = await stranger.GetAsync($"/api/projects/{project.Id}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Progress_ComparesEstimatedAndActualHours()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);
            var project = await CreateAsync(client, NewProject());

            var domainId = Guid.NewGuid();
            var doneTaskId = Guid.NewGuid();
            var openTaskId = Guid.NewGuid();
            await factory.SeedAsync(db =>
            {
                db.Domains.Add(new DomainEntities.Domain { Id = domainId, Name = "Dev", IsActive = true });
                db.Tasks.Add(new DomainEntities.Task
                {
                    Id = doneTaskId,
                    ProjectId = project.Id,
                    DomainId = domainId,
                    Title = "Design",
                    TaskType = "Flexible",
                    Status = "Done",
                    EstimatedMinutes = 120,
                    CompletedAt = DateTime.UtcNow
                });
                db.Tasks.Add(new DomainEntities.Task
                {
                    Id = openTaskId,
                    ProjectId = project.Id,
                    DomainId = domainId,
                    Title = "Build",
                    TaskType = "Flexible",
                    Status = "ToDo",
                    EstimatedMinutes = 60
                });
                db.FocusSessions.Add(new DomainEntities.FocusSession
                {
                    Id = Guid.NewGuid(),
                    TaskId = doneTaskId,
                    StartedAt = new DateTime(2026, 10, 2, 9, 0, 0),
                    EndedAt = new DateTime(2026, 10, 2, 10, 30, 0),
                    CaptureMethod = "Timer",
                    IsConfirmed = true
                });
                db.FocusSessions.Add(new DomainEntities.FocusSession
                {
                    Id = Guid.NewGuid(),
                    TaskId = openTaskId,
                    StartedAt = new DateTime(2026, 10, 3, 9, 0, 0),
                    EndedAt = new DateTime(2026, 10, 3, 9, 30, 0),
                    CaptureMethod = "Timer",
                    IsConfirmed = true
                });
            });

            var progress = await client.GetFromJsonAsync<ProjectProgressDto>($"/api/projects/{project.Id}/progress");

            Assert.Equal(2, progress!.TotalTasks);
            Assert.Equal(1, progress.CompletedTasks);
            Assert.Equal(50.0, progress.ProgressPercent);
            Assert.Equal(180, progress.EstimatedMinutes);
            Assert.Equal(120, progress.ActualMinutes);
            Assert.Equal(-60, progress.VarianceMinutes);
        }

        private static async Task<ProjectDto> CreateAsync(HttpClient client, ProjectRequestDto request)
        {
            var response = await client.PostAsJsonAsync("/api/projects", request);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
        }
    }
}
