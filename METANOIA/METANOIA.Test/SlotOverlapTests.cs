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
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // Slot của một người không được chồng giờ nhau (409 SLOT_OVERLAP). Chỉ slot đang chiếm giờ (Approved, Due, InProgress, Overrun) mới chặn
    public class SlotOverlapTests
    {
        private static readonly Guid DomainId = Guid.NewGuid();

        private static async Task<(HttpClient Client, Guid TaskId)> SetupAsync(ApiFactory factory, string subject = "user-1", bool seedDomain = true)
        {
            if (seedDomain)
            {
                await factory.SeedAsync(db => db.Domains.Add(new METANOIA.Domain.Entities.Domain { Id = DomainId, Name = $"Dev-{Guid.NewGuid()}", IsActive = true }));
            }

            var (client, _) = await TestHelpers.LoginAsync(factory, subject);
            var project = await (await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Website" })).Content.ReadFromJsonAsync<ProjectDto>();
            var task = await (await client.PostAsJsonAsync($"/api/projects/{project!.Id}/tasks", new TaskRequestDto
            {
                Title = "Viết báo cáo",
                TaskType = "Flexible",
                DomainId = DomainId,
                Deadline = new DateTimeOffset(2026, 10, 30, 17, 0, 0, TimeSpan.Zero),
                EstimatedMinutes = 120
            })).Content.ReadFromJsonAsync<TaskDto>();
            return (client, task!.Id);
        }

        private static async Task<Guid> SecondTaskAsync(HttpClient client, string title = "Việc khác")
        {
            var project = await (await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = $"Dự án {title}" })).Content.ReadFromJsonAsync<ProjectDto>();
            var task = await (await client.PostAsJsonAsync($"/api/projects/{project!.Id}/tasks", new TaskRequestDto
            {
                Title = title,
                TaskType = "Flexible",
                DomainId = DomainId,
                Deadline = new DateTimeOffset(2026, 10, 30, 17, 0, 0, TimeSpan.Zero),
                EstimatedMinutes = 60
            })).Content.ReadFromJsonAsync<TaskDto>();
            return task!.Id;
        }

        // Ngày cố định 2026-10-10 (UTC), giờ và phút tùy ý
        private static ScheduleSlotRequestDto Slot(int startHour, int startMinute, int endHour, int endMinute, string? status = "Approved") => new()
        {
            StartTime = new DateTimeOffset(2026, 10, 10, startHour, startMinute, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 10, 10, endHour, endMinute, 0, TimeSpan.Zero),
            IsManualOverride = true,
            Status = status
        };

        private static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid taskId, ScheduleSlotRequestDto request) =>
            client.PostAsJsonAsync($"/api/tasks/{taskId}/schedule-slots", request);

        private static async Task<ScheduleSlotDto> CreateOkAsync(HttpClient client, Guid taskId, ScheduleSlotRequestDto request)
        {
            var response = await CreateAsync(client, taskId, request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ScheduleSlotDto>())!;
        }

        private static Task<HttpResponseMessage> UpdateAsync(HttpClient client, Guid slotId, ScheduleSlotRequestDto request) =>
            client.PutAsJsonAsync($"/api/schedule-slots/{slotId}", request);

        private static async Task<JsonElement> ErrorAsync(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        // ================================================================ Tạo slot

        [Fact]
        public async Task CreateApprovedSlot_OverlappingAnActiveSlot_Returns409_AndCreatesNothing()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupAsync(factory);
            var a = await CreateOkAsync(client, taskId, Slot(7, 0, 8, 0));

            var response = await CreateAsync(client, taskId, Slot(7, 30, 9, 0));
            var error = await ErrorAsync(response);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("SLOT_OVERLAP", error.GetProperty("code").GetString());
            var overlap = Assert.Single(error.GetProperty("details").GetProperty("overlaps").EnumerateArray());
            Assert.Equal(a.Id, overlap.GetProperty("slotId").GetGuid());
            Assert.Equal("Viết báo cáo", overlap.GetProperty("taskTitle").GetString());
            Assert.Equal("Approved", overlap.GetProperty("status").GetString());
            Assert.Equal(1, await QueryAsync(factory, db => db.ScheduleSlots.CountAsync()));
        }

        [Theory]
        [InlineData(6, 0, 7, 0)]     // kết thúc đúng lúc slot kia bắt đầu
        [InlineData(8, 0, 9, 0)]     // bắt đầu đúng lúc slot kia kết thúc
        [InlineData(9, 0, 10, 0)]    // cách xa
        public async Task CreateApprovedSlot_OutsideOrTouching_IsAllowed(int sh, int sm, int eh, int em)
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupAsync(factory);
            await CreateOkAsync(client, taskId, Slot(7, 0, 8, 0));

            await CreateOkAsync(client, taskId, Slot(sh, sm, eh, em));
        }

        [Theory]
        [InlineData(7, 0, 8, 0)]     // trùng hẳn
        [InlineData(7, 15, 7, 45)]   // nằm gọn bên trong
        [InlineData(6, 0, 9, 0)]     // bao trùm
        [InlineData(6, 30, 7, 30)]   // lấn đầu
        public async Task CreateApprovedSlot_AnyKindOfIntersection_IsBlocked(int sh, int sm, int eh, int em)
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupAsync(factory);
            await CreateOkAsync(client, taskId, Slot(7, 0, 8, 0));

            Assert.Equal(HttpStatusCode.Conflict, (await CreateAsync(client, taskId, Slot(sh, sm, eh, em))).StatusCode);
        }

        [Fact]
        public async Task OverlapIsBlockedAcrossDifferentTasksAndProjectsOfTheSameUser()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupAsync(factory);
            var otherTaskId = await SecondTaskAsync(client);
            await CreateOkAsync(client, taskId, Slot(7, 0, 8, 0));

            Assert.Equal(HttpStatusCode.Conflict, (await CreateAsync(client, otherTaskId, Slot(7, 30, 9, 0))).StatusCode);
        }

        [Fact]
        public async Task ProposedSlots_DoNotOccupyTime_SoTheyNeitherBlockNorAreBlocked()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupAsync(factory);
            await CreateOkAsync(client, taskId, Slot(7, 0, 8, 0, status: null));          // Proposed (mặc định)

            await CreateOkAsync(client, taskId, Slot(7, 30, 9, 0, status: "Approved"));   // không bị Proposed chặn
            await CreateOkAsync(client, taskId, Slot(7, 15, 8, 30, status: "Proposed"));  // Proposed chồng Approved vẫn được tạo
        }

        [Fact]
        public async Task CancelledSlot_NoLongerBlocks()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupAsync(factory);
            var a = await CreateOkAsync(client, taskId, Slot(7, 0, 8, 0));
            Assert.Equal(HttpStatusCode.Conflict, (await CreateAsync(client, taskId, Slot(7, 30, 9, 0))).StatusCode);

            (await client.DeleteAsync($"/api/schedule-slots/{a.Id}")).EnsureSuccessStatusCode();

            await CreateOkAsync(client, taskId, Slot(7, 30, 9, 0));
        }

        [Fact]
        public async Task AnotherUsersSlot_NeverBlocks()
        {
            using var factory = new ApiFactory();
            var (alice, aliceTask) = await SetupAsync(factory, "alice");
            var (bob, bobTask) = await SetupAsync(factory, "bob", seedDomain: false);
            await CreateOkAsync(alice, aliceTask, Slot(7, 0, 8, 0));

            await CreateOkAsync(bob, bobTask, Slot(7, 0, 8, 0));
        }

        // ================================================================ Sửa slot

        [Fact]
        public async Task MovingASlotOntoAnotherActiveSlot_Returns409_AndKeepsTheOldTime()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupAsync(factory);
            await CreateOkAsync(client, taskId, Slot(7, 0, 8, 0));
            var b = await CreateOkAsync(client, taskId, Slot(9, 0, 10, 0));

            var response = await UpdateAsync(client, b.Id, Slot(7, 30, 9, 0));
            var error = await ErrorAsync(response);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("SLOT_OVERLAP", error.GetProperty("code").GetString());
            var stored = await QueryAsync(factory, db => db.ScheduleSlots.SingleAsync(s => s.Id == b.Id));
            Assert.Equal(new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc), stored.StartTime);
            Assert.Empty(await QueryAsync(factory, db => db.ScheduleChangeLogs.Where(l => l.ScheduleSlotId == b.Id && l.ChangeType == "Moved").ToListAsync()));
        }

        [Fact]
        public async Task MovingASlotToFreeTime_IsAllowed_AndTouchingIsAllowed()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupAsync(factory);
            await CreateOkAsync(client, taskId, Slot(7, 0, 8, 0));
            var b = await CreateOkAsync(client, taskId, Slot(9, 0, 10, 0));

            Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(client, b.Id, Slot(8, 0, 9, 0))).StatusCode);
        }

        [Fact]
        public async Task ResizingASlotWithinItsOwnRange_IsNotBlockedByItself()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupAsync(factory);
            var a = await CreateOkAsync(client, taskId, Slot(7, 0, 8, 0));

            Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(client, a.Id, Slot(7, 10, 8, 20))).StatusCode);
        }

        [Fact]
        public async Task ApprovingAProposedSlot_ThatOverlapsAnActiveSlot_IsBlocked()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupAsync(factory);
            await CreateOkAsync(client, taskId, Slot(7, 0, 8, 0));
            var proposed = await CreateOkAsync(client, taskId, Slot(7, 30, 9, 0, status: "Proposed"));

            var response = await UpdateAsync(client, proposed.Id, Slot(7, 30, 9, 0, status: "Approved"));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("Proposed", (await QueryAsync(factory, db => db.ScheduleSlots.SingleAsync(s => s.Id == proposed.Id))).Status);
        }

        [Fact]
        public async Task EditingOtherFieldsOfASlotThatAlreadyOverlaps_IsNotBlocked()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupAsync(factory);
            await CreateOkAsync(client, taskId, Slot(7, 0, 8, 0));
            var b = await CreateOkAsync(client, taskId, Slot(9, 0, 10, 0));
            // Dữ liệu cũ đã chồng giờ từ trước khi có luật: chỉnh trực tiếp trong database
            await factory.SeedAsync(db =>
            {
                var slot = db.ScheduleSlots.Single(s => s.Id == b.Id);
                slot.StartTime = new DateTime(2026, 10, 10, 7, 30, 0, DateTimeKind.Utc);
                slot.EndTime = new DateTime(2026, 10, 10, 8, 30, 0, DateTimeKind.Utc);
            });
            var same = Slot(7, 30, 8, 30);
            same.Explanation = "Chỉ đổi ghi chú";

            var response = await UpdateAsync(client, b.Id, same);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // ================================================================ Duyệt đề xuất

        private static async Task<(Guid ProposalId, Guid[] SlotIds)> SeedProposalAsync(ApiFactory factory, HttpClient client, Guid taskId, Guid userId, params (int Hour, int Minute)[] starts)
        {
            var proposalId = Guid.NewGuid();
            var slotIds = starts.Select(_ => Guid.NewGuid()).ToArray();
            await factory.SeedAsync(db =>
            {
                db.ScheduleProposals.Add(new ScheduleProposal
                {
                    Id = proposalId, UserId = userId, Level = 3, Status = "Pending", IsFeasible = true, Explanation = "Đề xuất thử", CreatedAt = DateTime.UtcNow
                });
                for (var i = 0; i < starts.Length; i++)
                {
                    var start = new DateTime(2026, 10, 10, starts[i].Hour, starts[i].Minute, 0, DateTimeKind.Utc);
                    db.ScheduleSlots.Add(new ScheduleSlot
                    {
                        Id = slotIds[i], TaskId = taskId, ScheduleProposalId = proposalId,
                        StartTime = start, EndTime = start.AddMinutes(60), ProposedMinutes = 60, Status = "Proposed"
                    });
                }
            });
            return (proposalId, slotIds);
        }

        private static async Task<Guid> UserIdAsync(ApiFactory factory, string subject) =>
            (await QueryAsync(factory, db => db.Users.SingleAsync(u => u.Email == $"{subject}@test.com"))).Id;

        [Fact]
        public async Task ApproveProposal_WithASlotOverlappingAnActiveSlot_Returns409_AndChangesNothing()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupAsync(factory);
            var userId = await UserIdAsync(factory, "user-1");
            await CreateOkAsync(client, taskId, Slot(7, 0, 8, 0));
            var (proposalId, slotIds) = await SeedProposalAsync(factory, client, taskId, userId, (7, 30), (10, 0));

            var response = await client.PostAsync($"/api/schedule/proposals/{proposalId}/approve", null);
            var error = await ErrorAsync(response);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("SLOT_OVERLAP", error.GetProperty("code").GetString());
            var overlap = Assert.Single(error.GetProperty("details").GetProperty("overlaps").EnumerateArray());
            Assert.Equal(slotIds[0], overlap.GetProperty("slotId").GetGuid());
            Assert.Equal("Pending", (await QueryAsync(factory, db => db.ScheduleProposals.SingleAsync(p => p.Id == proposalId))).Status);
            Assert.All(await QueryAsync(factory, db => db.ScheduleSlots.Where(s => slotIds.Contains(s.Id)).ToListAsync()), s => Assert.Equal("Proposed", s.Status));
        }

        [Fact]
        public async Task ApproveProposal_WithSkipConflicts_CancelsTheOverlappingSlot_AndApprovesTheRest()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupAsync(factory);
            var userId = await UserIdAsync(factory, "user-1");
            await CreateOkAsync(client, taskId, Slot(7, 0, 8, 0));
            var (proposalId, slotIds) = await SeedProposalAsync(factory, client, taskId, userId, (7, 30), (10, 0));

            var response = await client.PostAsync($"/api/schedule/proposals/{proposalId}/approve?skipConflicts=true", null);
            var dto = await response.Content.ReadFromJsonAsync<ScheduleProposalDto>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(slotIds[0], Assert.Single(dto!.SkippedOverlaps!).SlotId);
            var stored = await QueryAsync(factory, db => db.ScheduleSlots.Where(s => slotIds.Contains(s.Id)).ToListAsync());
            Assert.Equal("Cancelled", stored.Single(s => s.Id == slotIds[0]).Status);
            Assert.Equal("Approved", stored.Single(s => s.Id == slotIds[1]).Status);
        }

        [Fact]
        public async Task ApproveProposal_WhenEveryProposedSlotOverlaps_Returns400WithSkipConflicts()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupAsync(factory);
            var userId = await UserIdAsync(factory, "user-1");
            await CreateOkAsync(client, taskId, Slot(7, 0, 9, 0));
            var (proposalId, _) = await SeedProposalAsync(factory, client, taskId, userId, (7, 30), (8, 0));

            var response = await client.PostAsync($"/api/schedule/proposals/{proposalId}/approve?skipConflicts=true", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task ProposalConflictsPreview_ListsOverlaps_EvenWithoutGoogle()
        {
            using var factory = new ApiFactory();
            var (client, taskId) = await SetupAsync(factory);
            var userId = await UserIdAsync(factory, "user-1");
            await CreateOkAsync(client, taskId, Slot(7, 0, 8, 0));
            var (proposalId, slotIds) = await SeedProposalAsync(factory, client, taskId, userId, (7, 30), (10, 0));

            var preview = await client.GetFromJsonAsync<ProposalConflictsDto>($"/api/schedule/proposals/{proposalId}/conflicts");

            Assert.Equal("NotConnected", preview!.GoogleCheck);
            Assert.Equal(slotIds[0], Assert.Single(preview.Overlaps).SlotId);
        }
    }
}
