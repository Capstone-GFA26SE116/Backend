using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace METANOIA.Infrastructure.Services
{
    // Lưu file tài liệu dự án trên Azure Blob Storage. Container luôn ở chế độ riêng tư (không ai tải được nếu không qua API,
    // nơi đã kiểm tra chủ sở hữu). Tên blob là mã ngẫu nhiên giống LocalFileStorage nên cột StoragePath không đổi định dạng.
    //
    // Cấu hình:
    //   Storage:BlobConnectionString  (bắt buộc, bí mật: để trong App Settings của Azure, không commit)
    //   Storage:BlobContainer         (mặc định "project-documents", tự tạo nếu chưa có)
    public class AzureBlobFileStorage : IFileStorage
    {
        public const string ConnectionStringKey = "Storage:BlobConnectionString";
        public const string ContainerKey = "Storage:BlobContainer";
        public const string DefaultContainer = "project-documents";

        private static readonly FileExtensionContentTypeProvider ContentTypes = new();

        private readonly BlobContainerClient _container;
        private readonly ILogger<AzureBlobFileStorage> _logger;
        private readonly SemaphoreSlim _containerGate = new(1, 1);
        private volatile bool _containerReady;

        public AzureBlobFileStorage(IConfiguration configuration, ILogger<AzureBlobFileStorage> logger)
        {
            var connectionString = configuration[ConnectionStringKey];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"Cấu hình '{ConnectionStringKey}' chưa được thiết lập.");
            }

            var containerName = configuration[ContainerKey];
            _container = new BlobContainerClient(
                connectionString,
                string.IsNullOrWhiteSpace(containerName) ? DefaultContainer : containerName.Trim());
            _logger = logger;
        }

        public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        {
            await EnsureContainerAsync(cancellationToken);

            var blobName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
            var options = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = ContentTypeFor(extension) }
            };

            await _container.GetBlobClient(blobName).UploadAsync(content, options, cancellationToken);
            return blobName;
        }

        public Stream OpenRead(string storagePath)
        {
            var blob = _container.GetBlobClient(storagePath);

            // Kiểm tra trước để file đã mất báo FileNotFoundException (service đổi thành 404) thay vì lỗi Azure khó hiểu
            if (!blob.Exists().Value)
            {
                throw new FileNotFoundException("Không tìm thấy blob trong kho lưu trữ.", storagePath);
            }

            return blob.OpenRead();
        }

        public void Delete(string storagePath)
        {
            try
            {
                _container.GetBlobClient(storagePath).DeleteIfExists();
            }
            catch (Exception ex) when (ex is RequestFailedException or IOException)
            {
                // Dòng trong database đã xóa rồi; xóa blob lỗi chỉ để lại một file mồ côi, không nên làm hỏng request
                _logger.LogWarning(ex, "Không xóa được blob {BlobName}; file mồ côi cần dọn tay.", storagePath);
            }
        }

        // Tạo container (riêng tư) ở lần lưu đầu tiên; các lần sau không gọi lại
        private async Task EnsureContainerAsync(CancellationToken cancellationToken)
        {
            if (_containerReady)
            {
                return;
            }

            await _containerGate.WaitAsync(cancellationToken);
            try
            {
                if (!_containerReady)
                {
                    await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
                    _containerReady = true;
                }
            }
            finally
            {
                _containerGate.Release();
            }
        }

        private static string ContentTypeFor(string extension)
        {
            var name = "file" + (extension.StartsWith('.') ? extension : "." + extension);
            return ContentTypes.TryGetContentType(name, out var contentType) ? contentType : "application/octet-stream";
        }
    }
}
