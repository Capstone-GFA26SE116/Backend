using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class ProjectDocumentService : IProjectDocumentService
    {
        private const string FileType = "File";
        private const string LinkType = "Link";
        private const string OtherTag = "Other";
        private const int MaxNameLength = 255;
        private const int MaxLinkLength = 2000;
        private const long MaxFileBytes = 20L * 1024 * 1024;

        private static readonly HashSet<string> AllowedTags = new() { "Brief", "Contract", "Design", OtherTag };

        private readonly IProjectDocumentRepository _documentRepository;
        private readonly IFileStorage _fileStorage;
        private readonly IUnitOfWork _unitOfWork;

        public ProjectDocumentService(
            IProjectDocumentRepository documentRepository,
            IFileStorage fileStorage,
            IUnitOfWork unitOfWork)
        {
            _documentRepository = documentRepository;
            _fileStorage = fileStorage;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<ProjectDocumentDto>> GetDocumentsAsync(Guid userId, Guid projectId, string? search, string? tag, CancellationToken cancellationToken = default)
        {
            if (!await _documentRepository.ProjectOwnedByUserAsync(projectId, userId, cancellationToken))
            {
                throw new NotFoundException("Không tìm thấy dự án.");
            }

            if (tag is not null && !AllowedTags.Contains(tag))
            {
                throw new UserFriendlyException("Tag không hợp lệ. Chọn một trong: Brief, Contract, Design, Other.");
            }

            var documents = await _documentRepository.GetByProjectIdAsync(projectId, search, tag, cancellationToken);
            return documents.Select(ToDto).ToList();
        }

        public async Task<ProjectDocumentDto> GetDocumentAsync(Guid userId, Guid documentId, CancellationToken cancellationToken = default)
        {
            var document = await GetOwnedDocumentAsync(userId, documentId, cancellationToken);
            return ToDto(document);
        }

        public async Task<ProjectDocumentDto> CreateFileDocumentAsync(
            Guid userId,
            Guid projectId,
            string? name,
            string? tag,
            string originalFileName,
            long sizeBytes,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            await EnsureProjectOwnedAsync(userId, projectId, cancellationToken);

            if (sizeBytes <= 0 || sizeBytes > MaxFileBytes)
            {
                throw new UserFriendlyException("File phải có dung lượng từ 1 byte đến 20 MB.");
            }

            var document = new ProjectDocument
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                SourceType = FileType,
                Name = ResolveName(name, originalFileName),
                Tag = ResolveTag(tag),
                SizeBytes = sizeBytes,
                UploadedAt = DateTime.UtcNow
            };
            document.StoragePath = await _fileStorage.SaveAsync(content, Path.GetExtension(originalFileName), cancellationToken);

            await _documentRepository.AddAsync(document, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(document);
        }

        public async Task<ProjectDocumentDto> CreateLinkDocumentAsync(Guid userId, Guid projectId, ProjectDocumentRequestDto request, CancellationToken cancellationToken = default)
        {
            await EnsureProjectOwnedAsync(userId, projectId, cancellationToken);

            var document = new ProjectDocument
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                SourceType = LinkType,
                Name = ResolveName(request.Name, request.LinkUrl),
                Tag = ResolveTag(request.Tag),
                LinkUrl = ValidateLink(request.LinkUrl),
                UploadedAt = DateTime.UtcNow
            };

            await _documentRepository.AddAsync(document, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(document);
        }

        public async Task<ProjectDocumentDto> UpdateDocumentAsync(Guid userId, Guid documentId, ProjectDocumentRequestDto request, CancellationToken cancellationToken = default)
        {
            var document = await GetOwnedDocumentAsync(userId, documentId, cancellationToken);

            if (request.Name is not null)
            {
                document.Name = ValidateName(request.Name);
            }

            if (request.Tag is not null)
            {
                document.Tag = ResolveTag(request.Tag);
            }

            if (request.LinkUrl is not null)
            {
                if (document.SourceType == FileType)
                {
                    throw new UserFriendlyException("Tài liệu dạng File không có LinkUrl.");
                }
                document.LinkUrl = ValidateLink(request.LinkUrl);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(document);
        }

        public async Task DeleteDocumentAsync(Guid userId, Guid documentId, CancellationToken cancellationToken = default)
        {
            var document = await GetOwnedDocumentAsync(userId, documentId, cancellationToken);
            var storagePath = document.StoragePath;

            _documentRepository.Remove(document);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (storagePath is not null)
            {
                _fileStorage.Delete(storagePath);
            }
        }

        public async Task<DocumentDownload> OpenDownloadAsync(Guid userId, Guid documentId, CancellationToken cancellationToken = default)
        {
            var document = await GetOwnedDocumentAsync(userId, documentId, cancellationToken);

            if (document.SourceType != FileType || document.StoragePath is null)
            {
                throw new UserFriendlyException("Tài liệu này là liên kết, không có file để tải về.");
            }

            return new DocumentDownload(_fileStorage.OpenRead(document.StoragePath), document.Name);
        }

        private async Task EnsureProjectOwnedAsync(Guid userId, Guid projectId, CancellationToken cancellationToken)
        {
            if (!await _documentRepository.ProjectOwnedByUserAsync(projectId, userId, cancellationToken))
            {
                throw new NotFoundException("Không tìm thấy dự án.");
            }
        }

        private async Task<ProjectDocument> GetOwnedDocumentAsync(Guid userId, Guid documentId, CancellationToken cancellationToken)
        {
            return await _documentRepository.GetByIdForUserAsync(documentId, userId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy tài liệu.");
        }

        private static string ResolveName(string? name, string fallback)
        {
            var source = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(fallback) : name;
            return ValidateName(source);
        }

        private static string ValidateName(string name)
        {
            var trimmed = name.Trim();
            if (trimmed.Length == 0 || trimmed.Length > MaxNameLength)
            {
                throw new UserFriendlyException($"Tên tài liệu không được để trống và tối đa {MaxNameLength} ký tự.");
            }
            return trimmed;
        }

        private static string ResolveTag(string? tag)
        {
            var value = string.IsNullOrWhiteSpace(tag) ? OtherTag : tag;
            if (!AllowedTags.Contains(value))
            {
                throw new UserFriendlyException("Tag không hợp lệ. Chọn một trong: Brief, Contract, Design, Other.");
            }
            return value;
        }

        private static string ValidateLink(string? linkUrl)
        {
            if (string.IsNullOrWhiteSpace(linkUrl)
                || linkUrl.Length > MaxLinkLength
                || !Uri.TryCreate(linkUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new UserFriendlyException("LinkUrl phải là đường dẫn http hoặc https hợp lệ, tối đa 2000 ký tự.");
            }
            return linkUrl;
        }

        private static ProjectDocumentDto ToDto(ProjectDocument document)
        {
            return new ProjectDocumentDto
            {
                Id = document.Id,
                ProjectId = document.ProjectId,
                Name = document.Name,
                SourceType = document.SourceType,
                LinkUrl = document.LinkUrl,
                SizeBytes = document.SizeBytes,
                Tag = document.Tag,
                UploadedAt = UtcTime.ToApi(document.UploadedAt)
            };
        }
    }
}
