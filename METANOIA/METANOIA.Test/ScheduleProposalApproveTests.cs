using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using METANOIA.Test.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // POST /api/schedule/proposals/{id}/approve
    public class ScheduleProposalApproveTests
    {
        // Các giá trị mà bảng ScheduleChangeLog cho phép (khớp ScheduleChangeLogService và CHECK của database)
        private static readonly string[] AllowedChangeTypes = { "Created", "Moved", "Resized", "Deleted" };
        private static readonly string[] AllowedSources = { "Manual", "AI", "GoogleSync" };

        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static async Task<(HttpClient Client, Guid ProposalId, Guid[] SlotIds)> SeedPendingProposalAsync(ApiFactory factory, int slots = 2)
        {
            var domainId = Guid.NewGuid();
            await factory.SeedAsync(db => db.Domains.Add(new METANOIA.Domain.Entities.Domain { Id = domainId, Name = $"Dev-{Guid.NewGuid()}", IsActive = true }));
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            var project = await (await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Website" })).Content.ReadFromJsonAsync<ProjectDto>();
            var task = await (await client.PostAsJsonAsync($"/api/projects/{project!.Id}/tasks", new TaskRequestDto
            {
                Title = "Viết báo cáo",
                TaskType = "Flexible",
                DomainId = domainId,
                Deadline = new DateTimeOffset(2026, 10, 30, 17, 0, 0, TimeSpan.Zero),
                EstimatedMinutes = 120
            })).Content.ReadFromJsonAsync<TaskDto>();

            var proposalId = Guid.NewGuid();
            var slotIds = Enumerable.Range(0, slots).Select(_ => Guid.NewGuid()).ToArray();
            await factory.SeedAsync(db =>
            {
                db.ScheduleProposals.Add(new ScheduleProposal
                {
                    Id = proposalId,
                    UserId = auth.UserId,
                    Level = 3,
                    Status = "Pending",
                    IsFeasible = true,
                    Explanation = "Đề xuất thử",
                    CreatedAt = DateTime.UtcNow
                });

                for (var i = 0; i < slotIds.Length; i++)
                {
                    var start = new DateTime(2026, 10, 20, 9 + i * 2, 0, 0, DateTimeKind.Utc);
                    db.ScheduleSlots.Add(new ScheduleSlot
                    {
                        Id = slotIds[i],
                        TaskId = task!.Id,
                        ScheduleProposalId = proposalId,
                        StartTime = start,
                        EndTime = start.AddMinutes(60),
                        ProposedMinutes = 60,
                        Status = "Proposed"
                    });
                }
            });

            return (client, proposalId, slotIds);
        }

        [Fact]
        public async Task Approve_MarksTheProposalAndItsProposedSlotsApproved()
        {
            using var factory = new ApiFactory();
            var (client, proposalId, slotIds) = await SeedPendingProposalAsync(factory);

            var response = await client.PostAsync($"/api/schedule/proposals/{proposalId}/approve", null);
            var dto = await response.Content.ReadFromJsonAsync<ScheduleProposalDto>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Approved", dto!.Status);
            Assert.Equal(2, dto.Slots.Count);
            Assert.All(dto.Slots, s => Assert.Equal("Approved", s.Status));
            var stored = await QueryAsync(factory, db => db.ScheduleSlots.Where(s => slotIds.Contains(s.Id)).ToListAsync());
            Assert.All(stored, s => Assert.Equal("Approved", s.Status));
            Assert.Equal("Approved", (await QueryAsync(factory, db => db.ScheduleProposals.SingleAsync(p => p.Id == proposalId))).Status);
        }

        [Fact]
        public async Task Approve_DoesNotWriteChangeLogsThatBreakTheDatabaseChecks()
        {
            using var factory = new ApiFactory();
            var (client, proposalId, _) = await SeedPendingProposalAsync(factory);

            await client.PostAsync($"/api/schedule/proposals/{proposalId}/approve", null);
            var logs = await QueryAsync(factory, db => db.ScheduleChangeLogs.ToListAsync());

            // Trước đây duyệt ghi ChangeType "Approved" và Source "User": Postgres từ chối và API trả 500
            Assert.All(logs, l => Assert.Contains(l.ChangeType, AllowedChangeTypes));
            Assert.All(logs, l => Assert.Contains(l.Source, AllowedSources));
            Assert.DoesNotContain(logs, l => l.ChangeType == "Approved" || l.Source == "User");
        }

        [Fact]
        public async Task Approve_LeavesTheSlotTimesUntouched()
        {
            using var factory = new ApiFactory();
            var (client, proposalId, slotIds) = await SeedPendingProposalAsync(factory);
            var before = await QueryAsync(factory, db => db.ScheduleSlots.Where(s => slotIds.Contains(s.Id)).OrderBy(s => s.StartTime).ToListAsync());

            await client.PostAsync($"/api/schedule/proposals/{proposalId}/approve", null);
            var after = await QueryAsync(factory, db => db.ScheduleSlots.Where(s => slotIds.Contains(s.Id)).OrderBy(s => s.StartTime).ToListAsync());

            Assert.Equal(before.Select(s => (s.StartTime, s.EndTime)), after.Select(s => (s.StartTime, s.EndTime)));
        }

        [Fact]
        public async Task Approve_Twice_SecondCallIsRejectedWith400()
        {
            using var factory = new ApiFactory();
            var (client, proposalId, _) = await SeedPendingProposalAsync(factory);

            var first = await client.PostAsync($"/api/schedule/proposals/{proposalId}/approve", null);
            var second = await client.PostAsync($"/api/schedule/proposals/{proposalId}/approve", null);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        }

        [Fact]
        public async Task Approve_UnknownOrSomeoneElsesProposal_Returns404()
        {
            using var factory = new ApiFactory();
            var (_, proposalId, _) = await SeedPendingProposalAsync(factory);
            var (stranger, _) = await TestHelpers.LoginAsync(factory, "stranger");

            var unknown = await stranger.PostAsync($"/api/schedule/proposals/{Guid.NewGuid()}/approve", null);
            var notOwner = await stranger.PostAsync($"/api/schedule/proposals/{proposalId}/approve", null);

            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, notOwner.StatusCode);
        }

        [Fact]
        public async Task Approve_RequiresLogin()
        {
            using var factory = new ApiFactory();

            var response = await factory.CreateClient().PostAsync($"/api/schedule/proposals/{Guid.NewGuid()}/approve", null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Approve_SchedulesTheReminderJobsForEveryApprovedSlot()
        {
            using var factory = new ApiFactory();
            var (client, proposalId, slotIds) = await SeedPendingProposalAsync(factory);

            await client.PostAsync($"/api/schedule/proposals/{proposalId}/approve", null);

            // Trước đây duyệt đề xuất AI không hẹn job nào nên slot AI không bao giờ nhắc việc
            Assert.All(slotIds, id => Assert.Single(factory.Jobs.CallsFor("ScheduleAllJobsForSlot", id)));
        }
    }
}
