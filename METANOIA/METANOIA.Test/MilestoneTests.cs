using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Test.Infrastructure;
using Xunit;

namespace METANOIA.Test
{
    public class MilestoneTests
    {
        private static MilestoneRequestDto NewMilestone(int order = 1, string status = "Planned") => new()
        {
            Name = "Thiết kế",
            OrderIndex = order,
            DueDate = new DateOnly(2026, 10, 20),
            Status = status,
            RevisionCount = 0
        };

        private static async Task<Guid> CreateProjectAsync(HttpClient client)
        {
            var response = await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Website" });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ProjectDto>())!.Id;
        }

        [Fact]
        public async Task CreateMilestone_ThenList_ReturnsOrderedByOrderIndex()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);
            var projectId = await CreateProjectAsync(client);

            await client.PostAsJsonAsync($"/api/projects/{projectId}/milestones", NewMilestone(order: 2));
            await client.PostAsJsonAsync($"/api/projects/{projectId}/milestones", NewMilestone(order: 1));

            var list = await client.GetFromJsonAsync<List<MilestoneDto>>($"/api/projects/{projectId}/milestones");
            Assert.Equal(new[] { 1, 2 }, list!.Select(m => m.OrderIndex));
        }

        [Fact]
        public async Task DuplicateOrderIndexInSameProject_Returns400()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);
            var projectId = await CreateProjectAsync(client);
            await client.PostAsJsonAsync($"/api/projects/{projectId}/milestones", NewMilestone(order: 1));

            var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/milestones", NewMilestone(order: 1));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task InvalidStatus_Returns400()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);
            var projectId = await CreateProjectAsync(client);

            var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/milestones", NewMilestone(status: "Done"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task MovingToDelivered_SetsDeliveredAt()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);
            var projectId = await CreateProjectAsync(client);
            var created = await (await client.PostAsJsonAsync($"/api/projects/{projectId}/milestones", NewMilestone()))
                .Content.ReadFromJsonAsync<MilestoneDto>();

            var response = await client.PutAsJsonAsync($"/api/milestones/{created!.Id}", NewMilestone(status: "Delivered"));
            var updated = await response.Content.ReadFromJsonAsync<MilestoneDto>();

            Assert.Equal("Delivered", updated!.Status);
            Assert.NotNull(updated.DeliveredAt);
        }

        [Fact]
        public async Task OtherUsersProject_Returns404OnCreate()
        {
            using var factory = new ApiFactory();
            var (owner, _) = await TestHelpers.LoginAsync(factory, "owner");
            var projectId = await CreateProjectAsync(owner);

            var (stranger, _) = await TestHelpers.LoginAsync(factory, "stranger");
            var response = await stranger.PostAsJsonAsync($"/api/projects/{projectId}/milestones", NewMilestone());

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task DeleteMilestone_ThenGet_Returns404()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);
            var projectId = await CreateProjectAsync(client);
            var created = await (await client.PostAsJsonAsync($"/api/projects/{projectId}/milestones", NewMilestone()))
                .Content.ReadFromJsonAsync<MilestoneDto>();

            var delete = await client.DeleteAsync($"/api/milestones/{created!.Id}");
            var fetch = await client.GetAsync($"/api/milestones/{created.Id}");

            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, fetch.StatusCode);
        }
    }
}
