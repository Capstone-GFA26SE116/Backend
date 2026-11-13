using System.Net;
using System.Net.Http.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using METANOIA.Application.Dto;
using METANOIA.Infrastructure.Data;
using METANOIA.Infrastructure.Services;
using METANOIA.Test.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using DomainEntities = METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // "Số phút nhắc trước" (ReminderLeadMinutes) trong PUT /api/me/settings phải thật sự quyết định giờ nhắc trước giờ bắt đầu
    public class ReminderLeadTests
    {
        private sealed record Setup(HttpClient Client, Guid TaskId);

        // Ghi lại các job được hẹn thay cho Hangfire thật
        private sealed class RecordingJobClient : IBackgroundJobClient
        {
            public List<(string Method, DateTime At)> Scheduled { get; } = new();

            public string Create(Job job, IState state)
            {
                Scheduled.Add((job.Method.Name, ((ScheduledState)state).EnqueueAt));
                return Guid.NewGuid().ToString();
            }

            public bool ChangeState(string jobId, IState state, string expectedState) => true;
        }

        private static async Task<Setup> SetupAsync(ApiFactory factory)
        {
            var domainId = Guid.NewGuid();
            await factory.SeedAsync(db => db.Domains.Add(new DomainEntities.Domain { Id = domainId, Name = $"Dev-{Guid.NewGuid()}", IsActive = true }));
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");
            var project = (await (await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Nhắc việc" })).Content.ReadFromJsonAsync<ProjectDto>())!;
            var task = (await (await client.PostAsJsonAsync($"/api/projects/{project.Id}/tasks", new TaskRequestDto
            {
                Title = "Viết báo cáo", TaskType = "Flexible", DomainId = domainId,
                Deadline = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), EstimatedMinutes = 60
            })).Content.ReadFromJsonAsync<TaskDto>())!;
            return new Setup(client, task.Id);
        }

        private static Task<HttpResponseMessage> PutLeadAsync(Setup s, int lead) =>
            s.Client.PutAsJsonAsync("/api/me/settings", new UpdateSettingsRequestDto
            {
                WorkDayStart = new TimeOnly(8, 0), WorkDayEnd = new TimeOnly(18, 0), WorksOnWeekend = false,
                DefaultBufferMinutes = 10, ReminderLeadMinutes = lead, TimeZone = "Asia/Ho_Chi_Minh"
            });

        private static async Task<ScheduleSlotDto> NewSlotAsync(ApiFactory factory, Setup s, int startInMinutes)
        {
            var start = factory.Clock.GetUtcNow().AddMinutes(startInMinutes);
            var response = await s.Client.PostAsJsonAsync($"/api/tasks/{s.TaskId}/schedule-slots", new ScheduleSlotRequestDto
            { StartTime = start, EndTime = start.AddMinutes(30), IsManualOverride = true, Status = "Approved" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ScheduleSlotDto>())!;
        }

        // Chạy ScheduleJobService THẬT với Hangfire giả để biết nó hẹn PreStartReminder lúc mấy giờ
        private static async Task<List<(string Method, DateTime At)>> ScheduleWithRealServiceAsync(ApiFactory factory, Guid slotId)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recorder = new RecordingJobClient();
            var slot = await db.ScheduleSlots.AsNoTracking().SingleAsync(x => x.Id == slotId);
            new ScheduleJobService(recorder, db, NullLogger<ScheduleJobService>.Instance).ScheduleAllJobsForSlot(slot);
            return recorder.Scheduled;
        }

        [Theory]
        [InlineData(null, 15)]   // không đổi cài đặt: mặc định 15 phút
        [InlineData(30, 30)]
        [InlineData(5, 5)]
        public async Task PreStartReminder_IsScheduledAtTheUsersLeadMinutes(int? lead, int expectedMinutes)
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            if (lead is { } value) Assert.Equal(HttpStatusCode.OK, (await PutLeadAsync(s, value)).StatusCode);
            var slot = await NewSlotAsync(factory, s, startInMinutes: 180);

            var scheduled = await ScheduleWithRealServiceAsync(factory, slot.Id);

            var reminder = Assert.Single(scheduled, j => j.Method == "PreStartReminder");
            Assert.Equal(slot.StartTime.UtcDateTime.AddMinutes(-expectedMinutes), reminder.At.ToUniversalTime());
            Assert.Contains(scheduled, j => j.Method == "SlotDue");   // các job khác vẫn được hẹn
        }

        [Fact]
        public async Task LeadOfZero_MeansNoPreStartReminder_ButOtherJobsStay()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            Assert.Equal(HttpStatusCode.OK, (await PutLeadAsync(s, 0)).StatusCode);
            var slot = await NewSlotAsync(factory, s, startInMinutes: 180);

            var scheduled = await ScheduleWithRealServiceAsync(factory, slot.Id);

            Assert.DoesNotContain(scheduled, j => j.Method == "PreStartReminder");
            Assert.Contains(scheduled, j => j.Method == "SlotDue");
            Assert.Contains(scheduled, j => j.Method == "AutoTimeout");
        }

        [Fact]
        public async Task ChangingTheLead_ReschedulesUpcomingApprovedSlots_OnlyWhenItChanged()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            var upcoming = await NewSlotAsync(factory, s, startInMinutes: 180);
            var proposed = (await (await s.Client.PostAsJsonAsync($"/api/tasks/{s.TaskId}/schedule-slots", new ScheduleSlotRequestDto
            {
                StartTime = factory.Clock.GetUtcNow().AddHours(5), EndTime = factory.Clock.GetUtcNow().AddHours(6),
                IsManualOverride = true, Status = "Proposed"
            })).Content.ReadFromJsonAsync<ScheduleSlotDto>())!;
            var past = await NewSlotAsync(factory, s, startInMinutes: -120);
            var before = factory.Jobs.CallsFor("ScheduleAllJobsForSlot", upcoming.Id).Count();
            var beforeProposed = factory.Jobs.CallsFor("ScheduleAllJobsForSlot", proposed.Id).Count();
            var beforePast = factory.Jobs.CallsFor("ScheduleAllJobsForSlot", past.Id).Count();

            // Cùng giá trị (15): không hẹn lại gì
            await PutLeadAsync(s, 15);
            Assert.Equal(before, factory.Jobs.CallsFor("ScheduleAllJobsForSlot", upcoming.Id).Count());

            // Đổi giá trị: hẹn lại slot Approved sắp tới; không đụng slot Proposed hay slot đã qua giờ
            await PutLeadAsync(s, 30);
            Assert.Equal(before + 1, factory.Jobs.CallsFor("ScheduleAllJobsForSlot", upcoming.Id).Count());
            Assert.Equal(beforeProposed, factory.Jobs.CallsFor("ScheduleAllJobsForSlot", proposed.Id).Count());
            Assert.Equal(beforePast, factory.Jobs.CallsFor("ScheduleAllJobsForSlot", past.Id).Count());
        }

        [Fact]
        public async Task PreStartReminder_MessageStatesTheRealMinutesLeft()
        {
            using var factory = new ApiFactory();
            var s = await SetupAsync(factory);
            await PutLeadAsync(s, 30);
            var slot = await NewSlotAsync(factory, s, startInMinutes: 30);

            using (var scope = factory.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<SlotBackgroundJobs>().PreStartReminder(slot.Id);

            using var check = factory.Services.CreateScope();
            var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
            var note = await db.Notifications.SingleAsync();
            Assert.Contains("30 phút", note.Message);
            Assert.DoesNotContain("15 phút", note.Message);
        }
    }
}
