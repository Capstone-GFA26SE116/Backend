using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Infrastructure.Data;
using METANOIA.Test.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // Tạo / sửa / xóa mềm / khôi phục user bởi Admin (đọc đã có ở /api/admin/users)
    public class AdminUserCrudTests
    {
        private static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static async Task<(HttpClient Admin, AuthResponseDto AdminAuth, HttpClient User, AuthResponseDto UserAuth)> SetupAsync(ApiFactory factory)
        {
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, adminAuth) = await TestHelpers.LoginAsync(factory, "admin-1");
            var (user, userAuth) = await TestHelpers.LoginAsync(factory, "user-1");
            return (admin, adminAuth, user, userAuth);
        }

        private static AdminUserRequestDto NewUser(string email = "New.User@Example.com") => new()
        {
            Email = email,
            FullName = "Người Mới",
            Role = "Freelancer"
        };

        private static async Task<AdminUserDetailDto> CreateAsync(HttpClient admin, AdminUserRequestDto request)
        {
            var response = await admin.PostAsJsonAsync("/api/admin/users", request);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<AdminUserDetailDto>())!;
        }

        // ------------------------------------------------------------ Create

        [Fact]
        public async Task Create_MakesAFullyInitializedAccount_WithNormalizedEmail()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, _) = await SetupAsync(factory);

            var response = await admin.PostAsJsonAsync("/api/admin/users", NewUser());
            var created = await response.Content.ReadFromJsonAsync<AdminUserDetailDto>();

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("new.user@example.com", created!.Email);
            Assert.Equal("Người Mới", created.FullName);
            Assert.Equal("Freelancer", created.Role);
            Assert.False(created.OnboardingCompleted);
            Assert.Equal("Miễn phí", created.CurrentPlanName);
            Assert.Equal(1, created.ProjectCount);
            Assert.Equal("Asia/Ho_Chi_Minh", created.TimeZone);
            Assert.Equal(24, await QueryAsync(factory, db => db.UserCircadianProfiles.CountAsync(c => c.UserId == created.Id)));
            Assert.StartsWith("pending:", (await QueryAsync(factory, db => db.Users.SingleAsync(u => u.Id == created.Id))).GoogleSubjectId);
        }

        [Fact]
        public async Task Create_AcceptsRoleAndSettings()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, _) = await SetupAsync(factory);
            var request = NewUser();
            request.Role = "Admin";
            request.TimeZone = "Asia/Bangkok";
            request.WorkDayStart = new TimeOnly(7, 0);
            request.WorkDayEnd = new TimeOnly(15, 30);
            request.WorksOnWeekend = true;
            request.DefaultBufferMinutes = 5;
            request.ReminderLeadMinutes = 45;

            var created = await CreateAsync(admin, request);

            Assert.Equal("Admin", created.Role);
            Assert.Equal("Asia/Bangkok", created.TimeZone);
            Assert.Equal(new TimeOnly(7, 0), created.WorkDayStart);
            Assert.Equal(new TimeOnly(15, 30), created.WorkDayEnd);
            Assert.True(created.WorksOnWeekend);
            var stored = await QueryAsync(factory, db => db.Users.SingleAsync(u => u.Id == created.Id));
            Assert.Equal(5, stored.DefaultBufferMinutes);
            Assert.Equal(45, stored.ReminderLeadMinutes);
        }

        [Fact]
        public async Task Create_RejectsInvalidInput()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, _) = await SetupAsync(factory);
            await CreateAsync(admin, NewUser("taken@example.com"));

            AdminUserRequestDto With(Action<AdminUserRequestDto> change)
            {
                var request = NewUser("fresh@example.com");
                change(request);
                return request;
            }

            var bad = new[]
            {
                With(r => r.Email = null),
                With(r => r.Email = "not-an-email"),
                With(r => r.Email = "has space@x.com"),
                With(r => r.FullName = " "),
                With(r => r.FullName = new string('x', 101)),
                With(r => r.Email = "TAKEN@example.com"),        // trùng email, khác hoa thường
                With(r => r.Role = "NoSuchRole"),
                With(r => r.TimeZone = "Mars/Olympus"),
                With(r => { r.WorkDayStart = new TimeOnly(10, 0); r.WorkDayEnd = new TimeOnly(9, 0); }),
                With(r => r.DefaultBufferMinutes = 500),
                With(r => r.ReminderLeadMinutes = -1)
            };

            foreach (var request in bad)
            {
                var response = await admin.PostAsJsonAsync("/api/admin/users", request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
        }

        [Fact]
        public async Task Create_RequiresAdmin()
        {
            using var factory = new ApiFactory();
            var (_, _, user, _) = await SetupAsync(factory);

            var response = await user.PostAsJsonAsync("/api/admin/users", NewUser());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // ------------------------------------------------------------ Chủ nhân đăng nhập lần đầu nhận tài khoản

        [Fact]
        public async Task FirstGoogleLogin_WithTheSameEmail_AdoptsTheAccountCreatedByAnAdmin()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, _) = await SetupAsync(factory);
            var request = NewUser("preuser@test.com");
            request.FullName = "Tên do Admin đặt";
            var created = await CreateAsync(admin, request);

            var (_, auth) = await TestHelpers.LoginAsync(factory, "preuser"); // email = preuser@test.com
            var (_, again) = await TestHelpers.LoginAsync(factory, "preuser");

            Assert.Equal(created.Id, auth.UserId);
            Assert.Equal(created.Id, again.UserId);
            var stored = await QueryAsync(factory, db => db.Users.SingleAsync(u => u.Id == created.Id));
            Assert.Equal("preuser", stored.GoogleSubjectId);
            Assert.Equal("Tên do Admin đặt", stored.FullName);
            Assert.Equal(1, await QueryAsync(factory, db => db.Users.CountAsync(u => u.Email == "preuser@test.com")));
            Assert.Equal(1, await QueryAsync(factory, db => db.Projects.CountAsync(p => p.UserId == created.Id)));
        }

        [Fact]
        public async Task LoginWithAnEmailOfARealGoogleAccount_ByAnotherSubject_IsStillRejected()
        {
            using var factory = new ApiFactory();
            await SetupAsync(factory);

            var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/google", new GoogleLoginRequestDto
            {
                IdToken = "valid:someone-else:user-1@test.com:Kẻ mạo danh"
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // ------------------------------------------------------------ Update

        [Fact]
        public async Task Update_ChangesOnlyTheProvidedFields()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, userAuth) = await SetupAsync(factory);
            var before = await admin.GetFromJsonAsync<AdminUserDetailDto>($"/api/admin/users/{userAuth.UserId}");

            var response = await admin.PutAsJsonAsync($"/api/admin/users/{userAuth.UserId}", new AdminUserRequestDto
            {
                FullName = "Tên mới",
                TimeZone = "Asia/Bangkok"
            });
            var updated = await response.Content.ReadFromJsonAsync<AdminUserDetailDto>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Tên mới", updated!.FullName);
            Assert.Equal("Asia/Bangkok", updated.TimeZone);
            Assert.Equal(before!.Email, updated.Email);
            Assert.Equal(before.Role, updated.Role);
            Assert.Equal(before.WorkDayStart, updated.WorkDayStart);
            Assert.Equal(before.CurrentPlanName, updated.CurrentPlanName);
        }

        [Fact]
        public async Task Update_CanChangeEmailAndRole_ButNotToATakenEmailOrUnknownRole()
        {
            using var factory = new ApiFactory();
            var (admin, adminAuth, _, userAuth) = await SetupAsync(factory);

            var ok = await admin.PutAsJsonAsync($"/api/admin/users/{userAuth.UserId}", new AdminUserRequestDto { Email = "RENAMED@test.com", Role = "Admin" });
            var updated = await ok.Content.ReadFromJsonAsync<AdminUserDetailDto>();
            var taken = await admin.PutAsJsonAsync($"/api/admin/users/{userAuth.UserId}", new AdminUserRequestDto { Email = "admin-1@test.com" });
            var noRole = await admin.PutAsJsonAsync($"/api/admin/users/{userAuth.UserId}", new AdminUserRequestDto { Role = "Ghost" });
            var sameEmailAsSelf = await admin.PutAsJsonAsync($"/api/admin/users/{userAuth.UserId}", new AdminUserRequestDto { Email = "renamed@test.com" });

            Assert.Equal("renamed@test.com", updated!.Email);
            Assert.Equal("Admin", updated.Role);
            Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, noRole.StatusCode);
            Assert.Equal(HttpStatusCode.OK, sameEmailAsSelf.StatusCode);
            Assert.NotEqual(adminAuth.UserId, userAuth.UserId);
        }

        [Fact]
        public async Task Update_RejectsInvalidSettings_UnknownUser_AndDeletedUser()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, userAuth) = await SetupAsync(factory);
            var other = await CreateAsync(admin, NewUser("other@example.com"));
            await admin.DeleteAsync($"/api/admin/users/{other.Id}");

            var badHours = await admin.PutAsJsonAsync($"/api/admin/users/{userAuth.UserId}",
                new AdminUserRequestDto { WorkDayStart = new TimeOnly(23, 0) }); // sau giờ kết thúc hiện có (18:00)
            var badBuffer = await admin.PutAsJsonAsync($"/api/admin/users/{userAuth.UserId}", new AdminUserRequestDto { DefaultBufferMinutes = 999 });
            var badZone = await admin.PutAsJsonAsync($"/api/admin/users/{userAuth.UserId}", new AdminUserRequestDto { TimeZone = "x/y" });
            var unknown = await admin.PutAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}", new AdminUserRequestDto { FullName = "A" });
            var deleted = await admin.PutAsJsonAsync($"/api/admin/users/{other.Id}", new AdminUserRequestDto { FullName = "A" });

            Assert.Equal(HttpStatusCode.BadRequest, badHours.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, badBuffer.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, badZone.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, deleted.StatusCode);
        }

        [Fact]
        public async Task Update_TheLastAdminCannotBeDemoted_ButCanOnceThereIsAnotherAdmin()
        {
            using var factory = new ApiFactory();
            var (admin, adminAuth, _, userAuth) = await SetupAsync(factory);

            var blocked = await admin.PutAsJsonAsync($"/api/admin/users/{adminAuth.UserId}", new AdminUserRequestDto { Role = "Freelancer" });
            await admin.PutAsJsonAsync($"/api/admin/users/{userAuth.UserId}", new AdminUserRequestDto { Role = "Admin" });
            var allowed = await admin.PutAsJsonAsync($"/api/admin/users/{adminAuth.UserId}", new AdminUserRequestDto { Role = "Freelancer" });

            Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        [Fact]
        public async Task Update_RoleChangeAppliesToNewTokensOnly()
        {
            using var factory = new ApiFactory();
            var (admin, _, user, userAuth) = await SetupAsync(factory);

            await admin.PutAsJsonAsync($"/api/admin/users/{userAuth.UserId}", new AdminUserRequestDto { Role = "Admin" });
            var oldToken = await user.GetAsync("/api/admin/users");
            var refreshed = await (await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh",
                new RefreshRequestDto { RefreshToken = userAuth.RefreshToken })).Content.ReadFromJsonAsync<AuthResponseDto>();
            var promoted = factory.CreateClient();
            promoted.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshed!.AccessToken);
            var newToken = await promoted.GetAsync("/api/admin/users");

            Assert.Equal(HttpStatusCode.Forbidden, oldToken.StatusCode);
            Assert.Equal("Admin", refreshed.Role);
            Assert.Equal(HttpStatusCode.OK, newToken.StatusCode);
        }

        // ------------------------------------------------------------ Delete / Restore

        [Fact]
        public async Task Delete_SoftDeletesTheUser_BlocksTheirAccess_AndHidesThemFromTheList()
        {
            using var factory = new ApiFactory();
            var (admin, _, user, userAuth) = await SetupAsync(factory);

            var response = await admin.DeleteAsync($"/api/admin/users/{userAuth.UserId}");
            var tokenAfter = await user.GetAsync("/api/me");
            var refresh = await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new RefreshRequestDto { RefreshToken = userAuth.RefreshToken });
            var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/google", new GoogleLoginRequestDto { IdToken = "valid:user-1:user-1@test.com:Test user-1" });
            var list = await admin.GetFromJsonAsync<PagedResultDto<AdminUserListItemDto>>("/api/admin/users");
            var withDeleted = await admin.GetFromJsonAsync<PagedResultDto<AdminUserListItemDto>>("/api/admin/users?includeDeleted=true");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, tokenAfter.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
            Assert.DoesNotContain(list!.Items, u => u.Id == userAuth.UserId);
            Assert.NotNull(withDeleted!.Items.Single(u => u.Id == userAuth.UserId).DeletedAt);
            Assert.Equal(1, await QueryAsync(factory, db => db.Users.CountAsync(u => u.Id == userAuth.UserId)));
        }

        [Fact]
        public async Task Delete_CannotDeleteYourself_AndUnknownOrAlreadyDeletedIs404()
        {
            using var factory = new ApiFactory();
            var (admin, adminAuth, _, userAuth) = await SetupAsync(factory);

            var self = await admin.DeleteAsync($"/api/admin/users/{adminAuth.UserId}");
            var unknown = await admin.DeleteAsync($"/api/admin/users/{Guid.NewGuid()}");
            await admin.DeleteAsync($"/api/admin/users/{userAuth.UserId}");
            var twice = await admin.DeleteAsync($"/api/admin/users/{userAuth.UserId}");

            Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, twice.StatusCode);
            Assert.Null((await QueryAsync(factory, db => db.Users.SingleAsync(u => u.Id == adminAuth.UserId))).DeletedAt);
        }

        [Fact]
        public async Task Delete_RequiresAdmin()
        {
            using var factory = new ApiFactory();
            var (admin, adminAuth, user, _) = await SetupAsync(factory);

            var response = await user.DeleteAsync($"/api/admin/users/{adminAuth.UserId}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.NotNull(admin);
        }

        [Fact]
        public async Task Restore_BringsTheAccountBack_AndTheyCanLogInAgain()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, userAuth) = await SetupAsync(factory);
            await admin.DeleteAsync($"/api/admin/users/{userAuth.UserId}");

            var response = await admin.PostAsync($"/api/admin/users/{userAuth.UserId}/restore", null);
            var restored = await response.Content.ReadFromJsonAsync<AdminUserDetailDto>();
            var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/google", new GoogleLoginRequestDto { IdToken = "valid:user-1:user-1@test.com:Test user-1" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(restored!.DeletedAt);
            Assert.Equal("Miễn phí", restored.CurrentPlanName);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Equal(1, await QueryAsync(factory, db => db.Projects.CountAsync(p => p.UserId == userAuth.UserId && p.IsDefault)));
        }

        [Fact]
        public async Task Restore_OfAnActiveOrUnknownUser_IsRejected()
        {
            using var factory = new ApiFactory();
            var (admin, _, _, userAuth) = await SetupAsync(factory);

            var active = await admin.PostAsync($"/api/admin/users/{userAuth.UserId}/restore", null);
            var unknown = await admin.PostAsync($"/api/admin/users/{Guid.NewGuid()}/restore", null);

            Assert.Equal(HttpStatusCode.BadRequest, active.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        // ------------------------------------------------------------ Audit

        [Fact]
        public async Task UserChangesMadeByAnAdmin_AreWrittenToTheAuditLog()
        {
            using var factory = new ApiFactory();
            var (admin, adminAuth, _, userAuth) = await SetupAsync(factory);
            var created = await CreateAsync(admin, NewUser("audit@example.com"));
            var update = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userAuth.UserId}")
            {
                Content = JsonContent.Create(new AdminUserRequestDto { FullName = "Tên sau khi sửa" })
            };
            update.Headers.Add("X-Admin-Reason", "Khách yêu cầu đổi tên");
            (await admin.SendAsync(update)).EnsureSuccessStatusCode();
            await admin.DeleteAsync($"/api/admin/users/{created.Id}");

            var logs = (await admin.GetFromJsonAsync<PagedResultDto<AdminAuditLogDto>>("/api/admin/audit-logs?targetType=User&pageSize=100"))!.Items;

            Assert.Contains(logs, l => l.Action == "User.Create" && l.TargetId == created.Id);
            var edit = logs.Single(l => l.TargetId == userAuth.UserId);
            Assert.Equal("User.Update", edit.Action);
            Assert.Equal("Test user-1", edit.OldValue!.Value.GetProperty("FullName").GetString());
            Assert.Equal("Tên sau khi sửa", edit.NewValue!.Value.GetProperty("FullName").GetString());
            Assert.Equal("Khách yêu cầu đổi tên", edit.Reason);
            Assert.Equal(adminAuth.UserId, edit.AdminId);
            var deletion = logs.Single(l => l.TargetId == created.Id && l.Action == "User.Update");
            Assert.True(deletion.NewValue!.Value.TryGetProperty("DeletedAt", out _));
        }
    }
}
