using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Test.Infrastructure;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test
{
    // Các API liệt kê tài liệu dự án: của tôi (mọi dự án), theo userId, theo dự án, và toàn hệ thống cho Admin
    public class DocumentListTests
    {
        private static async Task<Guid> CreateProjectAsync(HttpClient client, string name)
        {
            var project = await (await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = name }))
                .Content.ReadFromJsonAsync<ProjectDto>();
            return project!.Id;
        }

        private static async Task<ProjectDocumentDto> AddLinkAsync(HttpClient client, Guid projectId, string name, string tag = "Other")
        {
            var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/documents/links",
                new ProjectDocumentRequestDto { Name = name, Tag = tag, LinkUrl = "https://example.com/" + Guid.NewGuid().ToString("N") });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ProjectDocumentDto>())!;
        }

        private static async Task<ProjectDocumentDto> AddFileAsync(HttpClient client, Guid projectId, string fileName, string tag = "Other")
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(new byte[] { 1, 2, 3 });
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "File", fileName);
            form.Add(new StringContent(tag), "Tag");

            var response = await client.PostAsync($"/api/projects/{projectId}/documents/files", form);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ProjectDocumentDto>())!;
        }

        private static async Task<List<ProjectDocumentListItemDto>> ListAsync(HttpClient client, string url)
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<List<ProjectDocumentListItemDto>>())!;
        }

        private static string[] Names(IEnumerable<ProjectDocumentDto> documents) => documents.Select(d => d.Name).OrderBy(n => n).ToArray();

        [Fact]
        public async Task MyDocuments_AcrossAllMyProjects_WithProjectName_AndNeverOthers()
        {
            using var factory = new ApiFactory();
            var (me, auth) = await TestHelpers.LoginAsync(factory, "user-1");
            var (other, _) = await TestHelpers.LoginAsync(factory, "user-2");
            var web = await CreateProjectAsync(me, "Website");
            var app = await CreateProjectAsync(me, "Mobile app");
            var theirs = await CreateProjectAsync(other, "Của người khác");
            await AddLinkAsync(me, web, "Brief khách");
            await AddFileAsync(me, app, "hop-dong.pdf");
            await AddLinkAsync(other, theirs, "Tài liệu của user 2");

            var mine = await ListAsync(me, "/api/documents");

            Assert.Equal(new[] { "Brief khách", "hop-dong.pdf" }, Names(mine));
            Assert.All(mine, d => Assert.Equal(auth.UserId, d.UserId));
            Assert.Equal("Website", mine.Single(d => d.Name == "Brief khách").ProjectName);
            Assert.Equal("Mobile app", mine.Single(d => d.Name == "hop-dong.pdf").ProjectName);
        }

        [Fact]
        public async Task MyDocuments_AreSortedNewestFirst_AndEmptyWhenThereAreNone()
        {
            using var factory = new ApiFactory();
            var (me, _) = await TestHelpers.LoginAsync(factory);
            Assert.Empty(await ListAsync(me, "/api/documents"));

            var project = await CreateProjectAsync(me, "Website");
            await AddLinkAsync(me, project, "Cũ");
            await Task.Delay(20);
            await AddLinkAsync(me, project, "Mới");

            var list = await ListAsync(me, "/api/documents");

            Assert.Equal(new[] { "Mới", "Cũ" }, list.Select(d => d.Name).ToArray());
        }

        [Fact]
        public async Task MyDocuments_FilterByProjectNameTagAndSourceType()
        {
            using var factory = new ApiFactory();
            var (me, _) = await TestHelpers.LoginAsync(factory);
            var web = await CreateProjectAsync(me, "Website");
            var app = await CreateProjectAsync(me, "Mobile app");
            await AddLinkAsync(me, web, "Hợp đồng thiết kế", "Contract");
            await AddFileAsync(me, web, "Brief-Khach.PDF", "Brief");
            await AddLinkAsync(me, app, "Figma app", "Design");

            Assert.Equal(new[] { "Brief-Khach.PDF", "Hợp đồng thiết kế" }, Names(await ListAsync(me, $"/api/documents?projectId={web}")));
            Assert.Equal(new[] { "Brief-Khach.PDF" }, Names(await ListAsync(me, "/api/documents?search=brief")));
            Assert.Equal(new[] { "Figma app" }, Names(await ListAsync(me, "/api/documents?tag=Design")));
            Assert.Equal(new[] { "Brief-Khach.PDF" }, Names(await ListAsync(me, "/api/documents?sourceType=File")));
            Assert.Equal(new[] { "Figma app", "Hợp đồng thiết kế" }, Names(await ListAsync(me, "/api/documents?sourceType=Link")));
            Assert.Equal(new[] { "Hợp đồng thiết kế" }, Names(await ListAsync(me, $"/api/documents?projectId={web}&sourceType=Link&tag=Contract")));
            Assert.Empty(await ListAsync(me, $"/api/documents?projectId={app}&tag=Contract"));
        }

        [Fact]
        public async Task ListFilters_RejectUnknownTagOrSourceType()
        {
            using var factory = new ApiFactory();
            var (me, auth) = await TestHelpers.LoginAsync(factory);

            Assert.Equal(HttpStatusCode.BadRequest, (await me.GetAsync("/api/documents?tag=Khac")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await me.GetAsync("/api/documents?sourceType=Video")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await me.GetAsync($"/api/documents/by-user/{auth.UserId}?tag=Khac")).StatusCode);
        }

        [Fact]
        public async Task ByUser_IsAllowedForMyself_ForbiddenForOthers_AndAllowedForAdmin()
        {
            using var factory = new ApiFactory();
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, _) = await TestHelpers.LoginAsync(factory, "admin-1");
            var (user, userAuth) = await TestHelpers.LoginAsync(factory, "user-1");
            var (other, otherAuth) = await TestHelpers.LoginAsync(factory, "user-2");
            await AddLinkAsync(user, await CreateProjectAsync(user, "P1"), "Của user 1");
            await AddLinkAsync(other, await CreateProjectAsync(other, "P2"), "Của user 2");

            var own = await ListAsync(user, $"/api/documents/by-user/{userAuth.UserId}");
            var forbidden = await user.GetAsync($"/api/documents/by-user/{otherAuth.UserId}");
            var adminView = await ListAsync(admin, $"/api/documents/by-user/{otherAuth.UserId}");
            var adminFiltered = await ListAsync(admin, $"/api/documents/by-user/{otherAuth.UserId}?search=user 2");

            Assert.Equal(new[] { "Của user 1" }, Names(own));
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.Equal(new[] { "Của user 2" }, Names(adminView));
            Assert.Equal(new[] { "Của user 2" }, Names(adminFiltered));
        }

        [Fact]
        public async Task ByProject_NewRouteReturnsTheSameAsTheProjectRoute_AndStaysOwnerOnly()
        {
            using var factory = new ApiFactory();
            var (me, _) = await TestHelpers.LoginAsync(factory, "user-1");
            var (other, _) = await TestHelpers.LoginAsync(factory, "user-2");
            var project = await CreateProjectAsync(me, "Website");
            await AddLinkAsync(me, project, "Brief", "Brief");
            await AddLinkAsync(me, project, "Hợp đồng", "Contract");

            var viaProjectRoute = await me.GetFromJsonAsync<List<ProjectDocumentDto>>($"/api/projects/{project}/documents?tag=Contract");
            var viaNewRoute = await me.GetFromJsonAsync<List<ProjectDocumentDto>>($"/api/documents/by-project/{project}?tag=Contract");

            Assert.Equal(Names(viaProjectRoute!), Names(viaNewRoute!));
            Assert.Equal(new[] { "Hợp đồng" }, Names(viaNewRoute!));
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/documents/by-project/{project}")).StatusCode);
        }

        [Fact]
        public async Task AllDocuments_AreAdminOnly_PagedAndFilterable()
        {
            using var factory = new ApiFactory();
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, _) = await TestHelpers.LoginAsync(factory, "admin-1");
            var (user, userAuth) = await TestHelpers.LoginAsync(factory, "user-1");
            var (other, _) = await TestHelpers.LoginAsync(factory, "user-2");
            var p1 = await CreateProjectAsync(user, "P1");
            var p2 = await CreateProjectAsync(other, "P2");
            for (var i = 1; i <= 3; i++) await AddLinkAsync(user, p1, $"U1-{i}", "Brief");
            for (var i = 1; i <= 2; i++) await AddLinkAsync(other, p2, $"U2-{i}", "Contract");

            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/documents/all")).StatusCode);

            var all = await admin.GetFromJsonAsync<PagedResultDto<ProjectDocumentListItemDto>>("/api/documents/all");
            Assert.Equal(5, all!.TotalCount);
            Assert.Equal(5, all.Items.Count);
            Assert.Equal(1, all.Page);
            Assert.Equal(20, all.PageSize);

            var page2 = await admin.GetFromJsonAsync<PagedResultDto<ProjectDocumentListItemDto>>("/api/documents/all?page=2&pageSize=2");
            Assert.Equal(5, page2!.TotalCount);
            Assert.Equal(2, page2.Items.Count);
            var page1 = await admin.GetFromJsonAsync<PagedResultDto<ProjectDocumentListItemDto>>("/api/documents/all?page=1&pageSize=2");
            Assert.Empty(page1!.Items.Select(d => d.Id).Intersect(page2.Items.Select(d => d.Id)));

            var ofUser = await admin.GetFromJsonAsync<PagedResultDto<ProjectDocumentListItemDto>>($"/api/documents/all?userId={userAuth.UserId}");
            Assert.Equal(3, ofUser!.TotalCount);
            Assert.All(ofUser.Items, d => Assert.Equal(userAuth.UserId, d.UserId));

            var contracts = await admin.GetFromJsonAsync<PagedResultDto<ProjectDocumentListItemDto>>("/api/documents/all?tag=Contract");
            Assert.Equal(new[] { "U2-1", "U2-2" }, Names(contracts!.Items));
        }

        [Fact]
        public async Task AllDocuments_RejectBadPaging()
        {
            using var factory = new ApiFactory();
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, _) = await TestHelpers.LoginAsync(factory, "admin-1");

            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/documents/all?page=0")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/documents/all?pageSize=101")).StatusCode);
        }

        [Fact]
        public async Task ListEndpoints_RequireLogin()
        {
            using var factory = new ApiFactory();
            var anonymous = factory.CreateClient();

            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/documents")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/documents/by-user/{Guid.NewGuid()}")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/documents/all")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/documents/by-project/{Guid.NewGuid()}")).StatusCode);
        }
    }
}
