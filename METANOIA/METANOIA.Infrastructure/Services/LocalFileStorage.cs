using METANOIA.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;

namespace METANOIA.Infrastructure.Services
{
    public class LocalFileStorage : IFileStorage
    {
        private readonly string _rootPath;

        public LocalFileStorage(IConfiguration configuration)
        {
            _rootPath = configuration["Storage:RootPath"]
                ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        }

        public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        {
            var relativePath = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
            var fullPath = Path.Combine(_rootPath, relativePath);

            Directory.CreateDirectory(_rootPath);
            await using var target = File.Create(fullPath);
            await content.CopyToAsync(target, cancellationToken);

            return relativePath;
        }

        public Stream OpenRead(string storagePath)
        {
            return File.OpenRead(Path.Combine(_rootPath, storagePath));
        }

        public void Delete(string storagePath)
        {
            var fullPath = Path.Combine(_rootPath, storagePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
    }
}
