using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Test.Infrastructure;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // GET /api/clients/filter?userId=&name=  và  GET /api/projects/filter?userId=&name=&status=&clientId=
    // Hai API danh sách cũ (GET /api/clients, GET /api/projects) giữ nguyên hành vi.
    public class ListFilterTests
    {
        private static async Task<ClientDto> CreateClientAsync(HttpClient client, string name)
        {
            var response = await client.PostAsJsonAsync("/api/clients", new ClientRequestDto { Name = name });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ClientDto>())!;
        }

        private static async Task<ProjectDto> CreateProjectAsync(HttpClient client, string name, Guid? clientId = null)
        {
            var response = await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = name, ClientId = clientId });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
        }

        private static async Task<string[]> ClientNamesAsync(HttpClient client, string url)
        {
            var list = await client.GetFromJsonAsync<List<ClientDto>>(url);
            return list!.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }

        private static async Task<string[]> ProjectNamesAsync(HttpClient client, string url)
        {
            var list = await client.GetFromJsonAsync<List<ProjectDto>>(url);
            return list!.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }

        private static string[] Sorted(params string[] names) => names.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        // ------------------------------------------------------------ Clients

        [Fact]
        public async Task ClientsFilter_NameIsPartial_CaseInsensitive_AndOnlyForTheCaller()
        {
            using var factory = new ApiFactory();
            var (user, _) = await TestHelpers.LoginAsync(factory, "user-1");
            var (other, _) = await TestHelpers.LoginAsync(factory, "user-2");
            await CreateClientAsync(user, "Công ty ABC");
            await CreateClientAsync(user, "abc Studio");
            await CreateClientAsync(user, "Xyz");
            await CreateClientAsync(other, "ABC của người khác");

            Assert.Equal(Sorted("Công ty ABC", "abc Studio"), await ClientNamesAsync(user, "/api/clients/filter?name=abc"));
            Assert.Equal(Sorted("Công ty ABC"), await ClientNamesAsync(user, "/api/clients/filter?name=%20TY%20a"));
            Assert.Equal(Sorted("ABC của người khác"), await ClientNamesAsync(other, "/api/clients/filter?name=abc"));
            Assert.Empty(await ClientNamesAsync(user, "/api/clients/filter?name=khong-co"));
        }

        [Fact]
        public async Task ClientsFilter_WithoutNameOrWithBlankName_ReturnsEverythingOfTheCaller()
        {
            using var factory = new ApiFactory();
            var (user, _) = await TestHelpers.LoginAsync(factory, "user-1");
            await CreateClientAsync(user, "A");
            await CreateClientAsync(user, "B");

            Assert.Equal(2, (await ClientNamesAsync(user, "/api/clients/filter")).Length);
            Assert.Equal(2, (await ClientNamesAsync(user, "/api/clients/filter?name=")).Length);
            Assert.Equal(2, (await ClientNamesAsync(user, "/api/clients/filter?name=%20%20")).Length);
        }

        [Fact]
        public async Task ClientsFilter_UserIdOfSomeoneElse_IsAdminOnly()
        {
            using var factory = new ApiFactory();
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, _) = await TestHelpers.LoginAsync(factory, "admin-1");
            var (user, userAuth) = await TestHelpers.LoginAsync(factory, "user-1");
            var (other, otherAuth) = await TestHelpers.LoginAsync(factory, "user-2");
            await CreateClientAsync(user, "Của user 1");
            await CreateClientAsync(other, "Của user 2");

            var ownId = await ClientNamesAsync(user, $"/api/clients/filter?userId={userAuth.UserId}");
            var forbidden = await user.GetAsync($"/api/clients/filter?userId={otherAuth.UserId}");
            var adminView = await ClientNamesAsync(admin, $"/api/clients/filter?userId={otherAuth.UserId}&name=user");
            var adminOwn = await ClientNamesAsync(admin, "/api/clients/filter");

            Assert.Equal(Sorted("Của user 1"), ownId);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.Equal(Sorted("Của user 2"), adminView);
            Assert.Empty(adminOwn);
        }

        [Fact]
        public async Task ClientsList_IsUnchanged_AndIgnoresFilterParameters()
        {
            using var factory = new ApiFactory();
            var (user, _) = await TestHelpers.LoginAsync(factory, "user-1");
            var (other, otherAuth) = await TestHelpers.LoginAsync(factory, "user-2");
            await CreateClientAsync(user, "Alpha");
            await CreateClientAsync(user, "Beta");
            await CreateClientAsync(other, "Của người khác");

            Assert.Equal(Sorted("Alpha", "Beta"), await ClientNamesAsync(user, "/api/clients"));
            Assert.Equal(Sorted("Alpha", "Beta"), await ClientNamesAsync(user, "/api/clients?name=alpha"));
            Assert.Equal(Sorted("Alpha", "Beta"), await ClientNamesAsync(user, $"/api/clients?userId={otherAuth.UserId}"));
        }

        // ------------------------------------------------------------ Projects

        [Fact]
        public async Task ProjectsFilter_NameIsPartial_CaseInsensitive_AndIncludesThePersonalProject()
        {
            using var factory = new ApiFactory();
            var (user, _) = await TestHelpers.LoginAsync(factory, "user-1");
            var (other, _) = await TestHelpers.LoginAsync(factory, "user-2");
            await CreateProjectAsync(user, "Website bán hàng");
            await CreateProjectAsync(user, "App di động");
            await CreateProjectAsync(other, "Website của người khác");

            Assert.Equal(Sorted("Website bán hàng"), await ProjectNamesAsync(user, "/api/projects/filter?name=WEBSITE"));
            Assert.Equal(Sorted("Cá nhân"), await ProjectNamesAsync(user, "/api/projects/filter?name=cá%20nh"));
            Assert.Equal(Sorted("Website của người khác"), await ProjectNamesAsync(other, "/api/projects/filter?name=website"));
            Assert.Empty(await ProjectNamesAsync(user, "/api/projects/filter?name=khong-co"));
        }

        [Fact]
        public async Task ProjectsFilter_StatusIsExact_AndWithoutItArchivedProjectsAreIncluded()
        {
            using var factory = new ApiFactory();
            var (user, _) = await TestHelpers.LoginAsync(factory, "user-1");
            await CreateProjectAsync(user, "Đang chạy");
            var old = await CreateProjectAsync(user, "Đã xong");
            await user.PostAsync($"/api/projects/{old.Id}/archive", null);

            Assert.Equal(Sorted("Cá nhân", "Đang chạy", "Đã xong"), await ProjectNamesAsync(user, "/api/projects/filter"));
            Assert.Equal(Sorted("Đã xong"), await ProjectNamesAsync(user, "/api/projects/filter?status=Archived"));
            Assert.Equal(Sorted("Cá nhân", "Đang chạy"), await ProjectNamesAsync(user, "/api/projects/filter?status=Active"));
            Assert.Equal(Sorted("Đã xong"), await ProjectNamesAsync(user, "/api/projects/filter?status=Archived&name=xong"));
            Assert.Empty(await ProjectNamesAsync(user, "/api/projects/filter?status=Archived&name=chạy"));
            Assert.Empty(await ProjectNamesAsync(user, "/api/projects/filter?status=NoSuchStatus"));
        }

        [Fact]
        public async Task ProjectsFilter_ByClient_AndCombinations()
        {
            using var factory = new ApiFactory();
            var (user, _) = await TestHelpers.LoginAsync(factory, "user-1");
            var acme = await CreateClientAsync(user, "Acme");
            var globex = await CreateClientAsync(user, "Globex");
            await CreateProjectAsync(user, "Acme website", acme.Id);
            await CreateProjectAsync(user, "Acme app", acme.Id);
            await CreateProjectAsync(user, "Globex website", globex.Id);

            Assert.Equal(Sorted("Acme app", "Acme website"), await ProjectNamesAsync(user, $"/api/projects/filter?clientId={acme.Id}"));
            Assert.Equal(Sorted("Acme website"), await ProjectNamesAsync(user, $"/api/projects/filter?clientId={acme.Id}&name=web"));
            Assert.Empty(await ProjectNamesAsync(user, $"/api/projects/filter?clientId={globex.Id}&name=acme"));
            Assert.Empty(await ProjectNamesAsync(user, $"/api/projects/filter?clientId={Guid.NewGuid()}"));
        }

        [Fact]
        public async Task ProjectsFilter_UserIdOfSomeoneElse_IsAdminOnly()
        {
            using var factory = new ApiFactory();
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, _) = await TestHelpers.LoginAsync(factory, "admin-1");
            var (user, userAuth) = await TestHelpers.LoginAsync(factory, "user-1");
            var (other, otherAuth) = await TestHelpers.LoginAsync(factory, "user-2");
            await CreateProjectAsync(user, "Dự án của user 1");
            await CreateProjectAsync(other, "Dự án của user 2");

            var ownId = await ProjectNamesAsync(user, $"/api/projects/filter?userId={userAuth.UserId}&name=dự");
            var forbidden = await user.GetAsync($"/api/projects/filter?userId={otherAuth.UserId}");
            var adminView = await ProjectNamesAsync(admin, $"/api/projects/filter?userId={otherAuth.UserId}&name=dự");

            Assert.Equal(Sorted("Dự án của user 1"), ownId);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.Equal(Sorted("Dự án của user 2"), adminView);
        }

        [Fact]
        public async Task ProjectsList_IsUnchanged_HidesArchivedByDefault_AndIgnoresFilterParameters()
        {
            using var factory = new ApiFactory();
            var (user, _) = await TestHelpers.LoginAsync(factory, "user-1");
            await CreateProjectAsync(user, "Đang chạy");
            var old = await CreateProjectAsync(user, "Đã xong");
            await user.PostAsync($"/api/projects/{old.Id}/archive", null);

            Assert.Equal(Sorted("Cá nhân", "Đang chạy"), await ProjectNamesAsync(user, "/api/projects"));
            Assert.Equal(Sorted("Cá nhân", "Đã xong", "Đang chạy"), await ProjectNamesAsync(user, "/api/projects?includeArchived=true"));
            Assert.Equal(Sorted("Cá nhân", "Đang chạy"), await ProjectNamesAsync(user, "/api/projects?name=xong&status=Archived"));
        }

        [Fact]
        public async Task FilterEndpoints_RequireLogin()
        {
            using var factory = new ApiFactory();
            var anonymous = factory.CreateClient();

            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/clients/filter")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/projects/filter")).StatusCode);
        }

        // ------------------------------------------------------------ Object client trong kết quả lọc dự án

        [Fact]
        public async Task ProjectsFilter_ReturnsTheClientObject_WhenTheProjectHasOne_AndNullWhenItHasNone()
        {
            using var factory = new ApiFactory();
            var (user, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            var acme = (await (await user.PostAsJsonAsync("/api/clients", new ClientRequestDto
            {
                Name = "Acme",
                ContactEmail = "hello@acme.com",
                Phone = "0901234567"
            })).Content.ReadFromJsonAsync<ClientDto>())!;
            var withClient = await CreateProjectAsync(user, "Có khách hàng", acme.Id);
            var withoutClient = await CreateProjectAsync(user, "Không có khách hàng");

            var items = (await user.GetFromJsonAsync<List<ProjectWithClientDto>>("/api/projects/filter"))!;

            var a = items.Single(p => p.Id == withClient.Id);
            Assert.Equal(acme.Id, a.ClientId);
            Assert.NotNull(a.Client);
            Assert.Equal(acme.Id, a.Client!.Id);
            Assert.Equal("Acme", a.Client.Name);
            Assert.Equal("hello@acme.com", a.Client.ContactEmail);
            Assert.Equal("0901234567", a.Client.Phone);
            Assert.Equal(auth.UserId, a.UserId);

            var b = items.Single(p => p.Id == withoutClient.Id);
            Assert.Null(b.ClientId);
            Assert.Null(b.Client);
            // Dự án "Cá nhân" cũng không có khách hàng
            Assert.All(items.Where(p => p.ClientId is null), p => Assert.Null(p.Client));
        }

        [Fact]
        public async Task ProjectsFilter_ClientIdIsOptional_WithoutItAllProjectsComeBack_WithItOnlyThatClients()
        {
            using var factory = new ApiFactory();
            var (user, _) = await TestHelpers.LoginAsync(factory, "user-1");
            var acme = await CreateClientAsync(user, "Acme");
            await CreateProjectAsync(user, "Của Acme", acme.Id);
            await CreateProjectAsync(user, "Lẻ không khách hàng");

            var all = (await user.GetFromJsonAsync<List<ProjectWithClientDto>>("/api/projects/filter"))!;
            var onlyAcme = (await user.GetFromJsonAsync<List<ProjectWithClientDto>>($"/api/projects/filter?clientId={acme.Id}"))!;

            Assert.Equal(Sorted("Cá nhân", "Của Acme", "Lẻ không khách hàng"), all.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            var only = Assert.Single(onlyAcme);
            Assert.Equal("Của Acme", only.Name);
            Assert.Equal("Acme", only.Client!.Name);
        }

        [Fact]
        public async Task ProjectsList_ReturnsEveryProject_WithTheClientObjectOrNull()
        {
            using var factory = new ApiFactory();
            var (user, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            var acme = (await (await user.PostAsJsonAsync("/api/clients", new ClientRequestDto { Name = "Acme", ContactEmail = "hello@acme.com" }))
                .Content.ReadFromJsonAsync<ClientDto>())!;
            await CreateProjectAsync(user, "Cần khách hàng", acme.Id);
            await CreateProjectAsync(user, "Không cần khách hàng");

            var items = (await user.GetFromJsonAsync<List<ProjectWithClientDto>>("/api/projects"))!;

            Assert.Equal(Sorted("Cá nhân", "Cần khách hàng", "Không cần khách hàng"), items.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            var withClient = items.Single(p => p.Name == "Cần khách hàng");
            Assert.Equal(acme.Id, withClient.ClientId);
            Assert.Equal("Acme", withClient.Client!.Name);
            Assert.Equal("hello@acme.com", withClient.Client.ContactEmail);
            Assert.Equal(auth.UserId, withClient.UserId);
            Assert.All(items.Where(p => p.ClientId is null), p => Assert.Null(p.Client));
            Assert.Equal(2, items.Count(p => p.ClientId is null));
        }

        [Fact]
        public async Task ProjectsList_StillHidesArchivedByDefault_AndShowsTheirClient()
        {
            using var factory = new ApiFactory();
            var (user, _) = await TestHelpers.LoginAsync(factory, "user-1");
            var acme = await CreateClientAsync(user, "Acme");
            var old = await CreateProjectAsync(user, "Đã xong", acme.Id);
            await user.PostAsync($"/api/projects/{old.Id}/archive", null);

            var hidden = (await user.GetFromJsonAsync<List<ProjectWithClientDto>>("/api/projects"))!;
            var shown = (await user.GetFromJsonAsync<List<ProjectWithClientDto>>("/api/projects?includeArchived=true"))!;

            Assert.DoesNotContain(hidden, p => p.Name == "Đã xong");
            Assert.Equal("Acme", shown.Single(p => p.Name == "Đã xong").Client!.Name);
        }

        [Fact]
        public async Task OtherProjectEndpoints_DoNotCarryTheClientObject()
        {
            using var factory = new ApiFactory();
            var (user, _) = await TestHelpers.LoginAsync(factory, "user-1");
            var acme = await CreateClientAsync(user, "Acme");
            var project = await CreateProjectAsync(user, "Của Acme", acme.Id);

            var single = await user.GetStringAsync($"/api/projects/{project.Id}");

            Assert.DoesNotContain("\"client\"", single);
            Assert.Contains("\"clientId\"", single);
        }
    }
}
