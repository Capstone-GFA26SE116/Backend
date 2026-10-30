using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Infrastructure.Services;
using METANOIA.Test.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace METANOIA.Test
{
    // Nơi lưu file tài liệu dự án: ổ đĩa khi chạy local, Azure Blob khi có Storage:BlobConnectionString
    public class StorageTests
    {
        private static async Task<(HttpClient Client, Guid ProjectId)> SetupProjectAsync(ApiFactory factory)
        {
            var (client, _) = await TestHelpers.LoginAsync(factory);
            var project = await (await client.PostAsJsonAsync("/api/projects", new ProjectRequestDto { Name = "Website" }))
                .Content.ReadFromJsonAsync<ProjectDto>();
            return (client, project!.Id);
        }

        private static async Task<ProjectDocumentDto> UploadAsync(HttpClient client, Guid projectId, byte[] bytes, string fileName)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "File", fileName);

            var response = await client.PostAsync($"/api/projects/{projectId}/documents/files", form);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ProjectDocumentDto>())!;
        }

        [Fact]
        public void WithoutABlobConnectionString_FilesAreStoredOnLocalDisk()
        {
            using var factory = new ApiFactory();
            factory.CreateClient();

            Assert.IsType<LocalFileStorage>(factory.Services.GetRequiredService<IFileStorage>());
        }

        [Fact]
        public void WithABlobConnectionString_FilesAreStoredInAzureBlob()
        {
            using var factory = new ApiFactory();
            // Chuỗi của trình giả lập Azurite: chỉ cần phân tích được, tạo đối tượng không gọi mạng
            factory.ExtraSettings[AzureBlobFileStorage.ConnectionStringKey] = "UseDevelopmentStorage=true";
            factory.CreateClient();

            Assert.IsType<AzureBlobFileStorage>(factory.Services.GetRequiredService<IFileStorage>());
        }

        [Fact]
        public async Task Download_WhenTheStoredFileIsGone_Returns404_NotAServerError()
        {
            using var factory = new ApiFactory();
            var (client, projectId) = await SetupProjectAsync(factory);
            var document = await UploadAsync(client, projectId, new byte[] { 1, 2, 3 }, "hop-dong.pdf");
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/documents/{document.Id}/download")).StatusCode);

            // Giống khi thư mục lưu file bị xóa lúc deploy lại: dòng trong database còn, file thì mất
            foreach (var file in Directory.GetFiles(factory.StorageRoot)) File.Delete(file);
            var response = await client.GetAsync($"/api/documents/{document.Id}/download");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Contains("tải file lên lại", body.RootElement.GetProperty("message").GetString());
        }
    }
}
