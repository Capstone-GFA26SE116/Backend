using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using METANOIA.Infrastructure.Services;
using METANOIA.Test.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // DELETE /api/me, DELETE /api/google-calendar/disconnect, POST /api/google-calendar/webhook, GET /api/calendar
    public class AccountAndCalendarTests
    {
        private const string WebhookUrl = "/api/google-calendar/webhook";

        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static async Task<Guid> SeedConnectionAsync(ApiFactory factory, Guid userId, string? watchChannelId = null, int events = 0)
        {
            var connectionId = Guid.NewGuid();
            await factory.SeedAsync(db =>
            {
                db.GoogleConnections.Add(new GoogleConnection
                {
                    Id = connectionId,
                    UserId = userId,
                    AccessTokenEncrypted = new byte[] { 1 },
                    RefreshTokenEncrypted = new byte[] { 1 },
                    TokenExpiresAt = DateTime.UtcNow.AddHours(1),
                    Status = "Active",
                    WatchChannelId = watchChannelId,
                    CreatedAt = DateTime.UtcNow
                });

                for (var i = 0; i < events; i++)
                {
                    db.GoogleCalendarEvents.Add(NewEvent(connectionId, $"event-{i}", new DateTime(2026, 10, 10, 8 + i, 0, 0, DateTimeKind.Utc), 30));
                }
            });
            return connectionId;
        }

        private static GoogleCalendarEvent NewEvent(Guid connectionId, string googleEventId, DateTime start, int minutes, string title = "Sự kiện", bool cancelled = false) => new()
        {
            Id = Guid.NewGuid(),
            GoogleConnectionId = connectionId,
            GoogleEventId = googleEventId,
            Title = title,
            StartTime = start,
            EndTime = start.AddMinutes(minutes),
            Etag = "etag",
            IsCancelled = cancelled,
            UpdatedAt = DateTime.UtcNow
        };

        private static HttpRequestMessage Webhook(string? channelId, string? token, string state = "exists")
        {
            var request = new HttpRequestMessage(HttpMethod.Post, WebhookUrl);
            if (channelId is not null) request.Headers.Add("X-Goog-Channel-ID", channelId);
            if (token is not null) request.Headers.Add("X-Goog-Channel-Token", token);
            request.Headers.Add("X-Goog-Resource-State", state);
            return request;
        }

        private static string ValidToken(string channelId) => GoogleWebhookToken.Create(channelId, ApiFactory.JwtKey);

        // ------------------------------------------------------------ DELETE /api/me

        [Fact]
        public async Task DeleteMe_SoftDeletes_RemovesGoogleLink_AndRejectsOldTokenImmediately()
        {
            using var factory = new ApiFactory();
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            await SeedConnectionAsync(factory, auth.UserId, events: 2);

            var response = await client.DeleteAsync("/api/me");
            var afterWithOldToken = await client.GetAsync("/api/me");

            var user = await QueryAsync(factory, db => db.Users.SingleAsync(u => u.Id == auth.UserId));
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.NotNull(user.DeletedAt);
            Assert.Equal(HttpStatusCode.Unauthorized, afterWithOldToken.StatusCode);
            Assert.Equal(0, await QueryAsync(factory, db => db.GoogleConnections.CountAsync(c => c.UserId == auth.UserId)));
            Assert.Equal(0, await QueryAsync(factory, db => db.GoogleCalendarEvents.CountAsync()));
        }

        [Fact]
        public async Task DeleteMe_KeepsBusinessData_AndBlocksLoginAgain()
        {
            using var factory = new ApiFactory();
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");

            await client.DeleteAsync("/api/me");
            var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/google", new GoogleLoginRequestDto
            {
                IdToken = "valid:user-1:user-1@test.com:Test user-1"
            });

            Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
            var error = await login.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            Assert.Contains("đã bị xóa", error!["message"]);
            Assert.Equal(1, await QueryAsync(factory, db => db.Projects.CountAsync(p => p.UserId == auth.UserId)));
            Assert.Equal(1, await QueryAsync(factory, db => db.Users.CountAsync()));
        }

        [Fact]
        public async Task DeleteMe_WithoutLogin_Returns401()
        {
            using var factory = new ApiFactory();

            var response = await factory.CreateClient().DeleteAsync("/api/me");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task DeleteMe_Twice_SecondCallIsRejectedAsUnauthorized()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");

            await client.DeleteAsync("/api/me");
            var second = await client.DeleteAsync("/api/me");

            Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        }

        // ------------------------------------------------------------ DELETE /api/google-calendar/disconnect

        [Fact]
        public async Task Disconnect_RemovesConnectionAndCachedEvents_OnlyForThatUser()
        {
            using var factory = new ApiFactory();
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            var (_, other) = await TestHelpers.LoginAsync(factory, "user-2");
            await SeedConnectionAsync(factory, auth.UserId, events: 3);
            await SeedConnectionAsync(factory, other.UserId, events: 2);

            var response = await client.DeleteAsync("/api/google-calendar/disconnect");
            var status = await client.GetFromJsonAsync<GoogleCalendarStatusDto>("/api/google-calendar/status");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.False(status!.IsConnected);
            Assert.Equal(0, await QueryAsync(factory, db => db.GoogleConnections.CountAsync(c => c.UserId == auth.UserId)));
            Assert.Equal(1, await QueryAsync(factory, db => db.GoogleConnections.CountAsync(c => c.UserId == other.UserId)));
            Assert.Equal(2, await QueryAsync(factory, db => db.GoogleCalendarEvents.CountAsync()));
        }

        [Fact]
        public async Task Disconnect_WhenNotConnected_IsHarmless()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");

            var response = await client.DeleteAsync("/api/google-calendar/disconnect");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task Disconnect_WithoutLogin_Returns401()
        {
            using var factory = new ApiFactory();

            var response = await factory.CreateClient().DeleteAsync("/api/google-calendar/disconnect");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // ------------------------------------------------------------ POST /api/google-calendar/webhook

        [Fact]
        public async Task Webhook_WithValidToken_SyncsTheOwnerOfTheChannel()
        {
            using var factory = new ApiFactory();
            var (_, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            await SeedConnectionAsync(factory, auth.UserId, watchChannelId: "channel-1");
            factory.GoogleSync.PassThrough = false;

            var response = await factory.CreateClient().SendAsync(Webhook("channel-1", ValidToken("channel-1")));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new[] { auth.UserId }, factory.GoogleSync.Calls.ToArray());
        }

        [Fact]
        public async Task Webhook_InitialSyncMessage_DoesNotTriggerSync()
        {
            using var factory = new ApiFactory();
            var (_, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            await SeedConnectionAsync(factory, auth.UserId, watchChannelId: "channel-1");
            factory.GoogleSync.PassThrough = false;

            var response = await factory.CreateClient().SendAsync(Webhook("channel-1", ValidToken("channel-1"), state: "sync"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(factory.GoogleSync.Calls);
        }

        [Fact]
        public async Task Webhook_WithWrongOrMissingToken_Returns403_AndDoesNothing()
        {
            using var factory = new ApiFactory();
            var (_, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            await SeedConnectionAsync(factory, auth.UserId, watchChannelId: "channel-1");
            factory.GoogleSync.PassThrough = false;
            var client = factory.CreateClient();

            var forged = await client.SendAsync(Webhook("channel-1", "forged"));
            var tokenOfAnotherChannel = await client.SendAsync(Webhook("channel-1", ValidToken("channel-2")));
            var noToken = await client.SendAsync(Webhook("channel-1", null));
            var noHeaders = await client.SendAsync(Webhook(null, null));

            Assert.All(new[] { forged, tokenOfAnotherChannel, noToken, noHeaders }, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
            Assert.Empty(factory.GoogleSync.Calls);
        }

        [Fact]
        public async Task Webhook_ForChannelNoLongerOwned_IsAcknowledgedAndIgnored()
        {
            using var factory = new ApiFactory();
            await TestHelpers.LoginAsync(factory, "user-1");
            factory.GoogleSync.PassThrough = false;

            var response = await factory.CreateClient().SendAsync(Webhook("old-channel", ValidToken("old-channel")));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(factory.GoogleSync.Calls);
        }

        [Fact]
        public async Task Webhook_WhenSyncFails_StillReturns200_SoGoogleDoesNotRetryStorm()
        {
            using var factory = new ApiFactory();
            var (_, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            await SeedConnectionAsync(factory, auth.UserId, watchChannelId: "channel-1");
            factory.GoogleSync.PassThrough = false;
            factory.GoogleSync.ThrowOnSync = true;

            var response = await factory.CreateClient().SendAsync(Webhook("channel-1", ValidToken("channel-1")));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Single(factory.GoogleSync.Calls);
        }

        [Fact]
        public async Task Webhook_AfterDisconnect_NoLongerSyncs()
        {
            using var factory = new ApiFactory();
            var (client, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            await SeedConnectionAsync(factory, auth.UserId, watchChannelId: "channel-1");
            factory.GoogleSync.PassThrough = false;
            await client.DeleteAsync("/api/google-calendar/disconnect");

            var response = await factory.CreateClient().SendAsync(Webhook("channel-1", ValidToken("channel-1")));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(factory.GoogleSync.Calls);
        }

        // ------------------------------------------------------------ GET /api/calendar

        private static readonly Guid DomainId = Guid.NewGuid();

        private static async Task<(HttpClient Client, Guid UserId, Guid TaskId)> SetupTaskAsync(ApiFactory factory, string subject = "user-1")
        {
            await factory.SeedAsync(db =>
            {
                if (!db.Domains.Any(d => d.Id == DomainId))
                {
                    db.Domains.Add(new METANOIA.Domain.Entities.Domain { Id = DomainId, Name = $"Dev-{Guid.NewGuid()}", IsActive = true });
                }
            });

            var (client, auth) = await TestHelpers.LoginAsync(factory, subject);
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
            return (client, auth.UserId, task!.Id);
        }

        private static async Task<ScheduleSlotDto> CreateSlotAsync(HttpClient client, Guid taskId, int hour, int minutes = 60)
        {
            var start = new DateTimeOffset(2026, 10, 10, hour, 0, 0, TimeSpan.Zero);
            var response = await client.PostAsJsonAsync($"/api/tasks/{taskId}/schedule-slots", new ScheduleSlotRequestDto
            {
                StartTime = start,
                EndTime = start.AddMinutes(minutes)
            });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ScheduleSlotDto>())!;
        }

        private const string Day = "from=2026-10-10T00:00:00Z&to=2026-10-11T00:00:00Z";

        [Fact]
        public async Task Calendar_MergesSlotsWithGoogleEvents_SortedByStart()
        {
            using var factory = new ApiFactory();
            var (client, userId, taskId) = await SetupTaskAsync(factory);
            var slot = await CreateSlotAsync(client, taskId, hour: 9, minutes: 90);
            var connectionId = await SeedConnectionAsync(factory, userId);
            await factory.SeedAsync(db => db.GoogleCalendarEvents.Add(
                NewEvent(connectionId, "meeting", new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc), 30, "Họp khách hàng")));

            var calendar = await client.GetFromJsonAsync<CalendarDto>($"/api/calendar?{Day}");

            Assert.Equal("Active", calendar!.GoogleCalendarStatus);
            Assert.Collection(calendar.Items,
                first =>
                {
                    Assert.Equal("Google", first.Source);
                    Assert.Equal("Họp khách hàng", first.Title);
                    Assert.Null(first.TaskId);
                },
                second =>
                {
                    Assert.Equal("Slot", second.Source);
                    Assert.Equal("Viết báo cáo", second.Title);
                    Assert.Equal(slot.Id.ToString(), second.Id);
                    Assert.Equal(taskId, second.TaskId);
                    Assert.Equal("Proposed", second.Status);
                    Assert.NotNull(second.ProjectId);
                    Assert.Equal(TimeSpan.FromMinutes(90), second.End - second.Start);
                });
        }

        [Fact]
        public async Task Calendar_OmitsOutOfRange_CancelledAndDuplicateEvents_AndOtherUsersData()
        {
            using var factory = new ApiFactory();
            var (client, userId, taskId) = await SetupTaskAsync(factory);
            var (otherClient, otherUserId, otherTaskId) = await SetupTaskAsync(factory, "user-2");
            var syncedSlot = await CreateSlotAsync(client, taskId, hour: 9);
            await CreateSlotAsync(otherClient, otherTaskId, hour: 9);
            var connectionId = await SeedConnectionAsync(factory, userId);
            var otherConnectionId = await SeedConnectionAsync(factory, otherUserId);
            await factory.SeedAsync(db =>
            {
                db.ScheduleSlots.Single(s => s.Id == syncedSlot.Id).GoogleEventId = "copy-of-slot";
                db.GoogleCalendarEvents.AddRange(
                    NewEvent(connectionId, "copy-of-slot", new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc), 60, "Bản sao của slot"),
                    NewEvent(connectionId, "cancelled", new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc), 30, "Đã hủy", cancelled: true),
                    NewEvent(connectionId, "next-week", new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc), 30, "Tuần sau"),
                    NewEvent(otherConnectionId, "someone-else", new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc), 30, "Của người khác"),
                    NewEvent(connectionId, "kept", new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc), 30, "Giữ lại"));
            });

            var calendar = await client.GetFromJsonAsync<CalendarDto>($"/api/calendar?{Day}");

            Assert.Equal(new[] { "Viết báo cáo", "Giữ lại" }, calendar!.Items.Select(i => i.Title).ToArray());
        }

        [Fact]
        public async Task Calendar_HidesCancelledSlots_UnlessAsked()
        {
            using var factory = new ApiFactory();
            var (client, _, taskId) = await SetupTaskAsync(factory);
            await CreateSlotAsync(client, taskId, hour: 9);
            var cancelled = await CreateSlotAsync(client, taskId, hour: 11);
            await client.DeleteAsync($"/api/schedule-slots/{cancelled.Id}");

            var normal = await client.GetFromJsonAsync<CalendarDto>($"/api/calendar?{Day}");
            var all = await client.GetFromJsonAsync<CalendarDto>($"/api/calendar?{Day}&includeCancelled=true");

            Assert.Single(normal!.Items);
            Assert.Equal(2, all!.Items.Count);
            Assert.Contains(all.Items, i => i.Status == "Cancelled");
        }

        [Fact]
        public async Task Calendar_WithoutGoogle_StillReturnsSlots_AndReportsNotConnected()
        {
            using var factory = new ApiFactory();
            var (client, _, taskId) = await SetupTaskAsync(factory);
            await CreateSlotAsync(client, taskId, hour: 9);

            var calendar = await client.GetFromJsonAsync<CalendarDto>($"/api/calendar?{Day}");

            Assert.Equal("NotConnected", calendar!.GoogleCalendarStatus);
            Assert.Single(calendar.Items);
        }

        [Fact]
        public async Task Calendar_RejectsBadRanges_AndAnonymousCalls()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory, "user-1");

            var reversed = await client.GetAsync("/api/calendar?from=2026-10-11T00:00:00Z&to=2026-10-10T00:00:00Z");
            var tooLong = await client.GetAsync("/api/calendar?from=2026-01-01T00:00:00Z&to=2026-12-31T00:00:00Z");
            var anonymous = await factory.CreateClient().GetAsync($"/api/calendar?{Day}");

            Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
    }
}
