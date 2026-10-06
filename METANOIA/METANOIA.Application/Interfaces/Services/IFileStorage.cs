namespace METANOIA.Application.Interfaces.Services
{
    public interface IFileStorage
    {
        Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default);

        Stream OpenRead(string storagePath);

        void Delete(string storagePath);
    }
}
