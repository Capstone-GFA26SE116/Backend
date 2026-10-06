using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Test.Infrastructure;
using Xunit;

namespace METANOIA.Test
{
    public class DocumentTests
    {
        private static async Task<(HttpClient Client, Guid ProjectId)> SetupProjectAsync(ApiFactory factory, string subject = "user-1")
        {
            var (client, _) = await TestHelpers.LoginAsync(factory, subject);
            var project = await (await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Website" }))
                .Content.ReadFromJsonAsync<ProjectDto>();
            return (client, project!.Id);
        }

        private static MultipartFormDataContent UploadForm(byte[] bytes, string fileName, string? name = null, string? tag = null)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "File", fileName);
            if (name is not null) form.Add(new StringContent(name), "Name");
            if (tag is not null) form.Add(new StringContent(tag), "Tag");
            return form;
        }

        [Fact]
        public async Task CreateLink_ThenGet_ReturnsLinkDocument()
        {
            using var factory = new ApiFactory();
            var (client, projectId) = await SetupProjectAsync(factory);

            var created = await client.PostAsJsonAsync($"/api/projects/{projectId}/documents/links",
                new ProjectDocumentRequestDto { Name = "Brief Figma", Tag = "Design", LinkUrl = "https://figma.com/file/abc" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var dto = (await created.Content.ReadFromJsonAsync<ProjectDocumentDto>())!;

            Assert.Equal("Link", dto.SourceType);
            Assert.Equal("https://figma.com/file/abc", dto.LinkUrl);
            Assert.Null(dto.SizeBytes);
        }

        [Fact]
        public async Task CreateLink_WithNonHttpUrl_Returns400()
        {
            using var factory = new ApiFactory();
            var (client, projectId) = await SetupProjectAsync(factory);

            var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/documents/links",
                new ProjectDocumentRequestDto { LinkUrl = "ftp://files.example.com/a" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task UploadFile_ThenDownload_ReturnsSameBytes()
        {
            using var factory = new ApiFactory();
            var (client, projectId) = await SetupProjectAsync(factory);
            var bytes = new byte[] { 1, 2, 3, 4, 5 };

            var upload = await client.PostAsync($"/api/projects/{projectId}/documents/files", UploadForm(bytes, "contract.pdf", tag: "Contract"));
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
            var dto = (await upload.Content.ReadFromJsonAsync<ProjectDocumentDto>())!;

            Assert.Equal("File", dto.SourceType);
            Assert.Equal("contract.pdf", dto.Name);
            Assert.Equal(5, dto.SizeBytes);

            var download = await client.GetAsync($"/api/documents/{dto.Id}/download");
            Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        }

        [Fact]
        public async Task UploadFileOverTwentyMegabytes_Returns400()
        {
            using var factory = new ApiFactory();
            var (client, projectId) = await SetupProjectAsync(factory);
            var tooLarge = new byte[20 * 1024 * 1024 + 1];

            var response = await client.PostAsync($"/api/projects/{projectId}/documents/files", UploadForm(tooLarge, "big.zip"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task LinkDocument_CannotBeDownloaded()
        {
            using var factory = new ApiFactory();
            var (client, projectId) = await SetupProjectAsync(factory);
            var link = await (await client.PostAsJsonAsync($"/api/projects/{projectId}/documents/links",
                new ProjectDocumentRequestDto { LinkUrl = "https://example.com" })).Content.ReadFromJsonAsync<ProjectDocumentDto>();

            var response = await client.GetAsync($"/api/documents/{link!.Id}/download");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task SearchByName_IgnoresCase_AndFiltersByTag()
        {
            using var factory = new ApiFactory();
            var (client, projectId) = await SetupProjectAsync(factory);
            await client.PostAsJsonAsync($"/api/projects/{projectId}/documents/links",
                new ProjectDocumentRequestDto { Name = "Hợp đồng khách", Tag = "Contract", LinkUrl = "https://a.test" });
            await client.PostAsJsonAsync($"/api/projects/{projectId}/documents/links",
                new ProjectDocumentRequestDto { Name = "Moodboard", Tag = "Design", LinkUrl = "https://b.test" });

            var search = await client.GetFromJsonAsync<List<ProjectDocumentDto>>($"/api/projects/{projectId}/documents?search=HỢP");
            var byTag = await client.GetFromJsonAsync<List<ProjectDocumentDto>>($"/api/projects/{projectId}/documents?tag=Design");

            Assert.Single(search!);
            Assert.Equal("Hợp đồng khách", search![0].Name);
            Assert.Single(byTag!);
            Assert.Equal("Moodboard", byTag![0].Name);
        }

        [Fact]
        public async Task DeleteFileDocument_ThenDownload_Returns404()
        {
            using var factory = new ApiFactory();
            var (client, projectId) = await SetupProjectAsync(factory);
            var dto = await (await client.PostAsync($"/api/projects/{projectId}/documents/files", UploadForm([9, 9], "a.txt")))
                .Content.ReadFromJsonAsync<ProjectDocumentDto>();

            var delete = await client.DeleteAsync($"/api/documents/{dto!.Id}");
            var download = await client.GetAsync($"/api/documents/{dto.Id}/download");

            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        }

        [Fact]
        public async Task OtherUsersDocument_Returns404()
        {
            using var factory = new ApiFactory();
            var (owner, projectId) = await SetupProjectAsync(factory, "owner");
            var link = await (await owner.PostAsJsonAsync($"/api/projects/{projectId}/documents/links",
                new ProjectDocumentRequestDto { LinkUrl = "https://example.com" })).Content.ReadFromJsonAsync<ProjectDocumentDto>();

            var (stranger, _) = await TestHelpers.LoginAsync(factory, "stranger");
            var response = await stranger.GetAsync($"/api/documents/{link!.Id}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
