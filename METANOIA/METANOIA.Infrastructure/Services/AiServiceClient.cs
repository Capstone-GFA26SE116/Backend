using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace METANOIA.Infrastructure.Services
{
    public class AiServiceClient : IAiServiceClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AiServiceClient> _logger;

        public AiServiceClient(
            HttpClient httpClient,
            IOptions<AiServiceOptions> options,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AiServiceClient> logger)
        {
            _httpClient = httpClient;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;

            var baseUrl = options.Value.BaseUrl?.TrimEnd('/') ?? "http://127.0.0.1:8000";
            _httpClient.BaseAddress = new Uri(baseUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds > 0 ? options.Value.TimeoutSeconds : 30);
        }

        public async Task<AiLearningResponseDto?> RecordTaskCompletionAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "/learning/task-completed");
                ApplyAuthorizationHeader(request);

                var payload = new { task_id = taskId };
                request.Content = JsonContent.Create(payload, options: JsonOptions);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("AI Service record task completion failed: StatusCode={StatusCode}, Body={Body}", response.StatusCode, errorContent);
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<AiLearningResponseDto>(JsonOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                // Thao tác học máy là phi chặn (non-blocking), ghi log và tiếp tục để không làm hỏng việc cập nhật task của user
                _logger.LogError(ex, "Lỗi kết nối khi gọi AI Service FR-17 cho TaskId={TaskId}", taskId);
                return null;
            }
        }

        public async Task<AiScheduleProposalResponseDto> ProposeScheduleAsync(AiScheduleProposeRequestDto request, CancellationToken cancellationToken = default)
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/schedule/propose");
            ApplyAuthorizationHeader(httpRequest);

            var payload = new
            {
                task_ids = request.TaskIds,
                language = string.IsNullOrWhiteSpace(request.Language) ? "vi" : request.Language
            };
            httpRequest.Content = JsonContent.Create(payload, options: JsonOptions);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi kết nối tới AI Service khi đề xuất lịch trình");
                throw new UserFriendlyException("Không thể kết nối đến hệ thống AI xếp lịch. Vui lòng thử lại sau.");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new UserFriendlyException("Bạn đã dùng hết hạn mức gọi AI trong ngày (tối đa 50 lượt/ngày). Vui lòng thử lại vào ngày mai hoặc nâng cấp gói cước.");
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                throw new UserFriendlyException("Tài khoản của bạn chưa có gói dịch vụ nào đang hoạt động.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("AI Service propose schedule returned error: StatusCode={StatusCode}, Body={Body}", response.StatusCode, errorText);
                throw new UserFriendlyException("Hệ thống AI gặp sự cố khi xử lý lịch trình. Vui lòng thử lại sau.");
            }

            var result = await response.Content.ReadFromJsonAsync<AiScheduleProposalResponseDto>(JsonOptions, cancellationToken);
            return result ?? throw new UserFriendlyException("Phản hồi từ AI Service không hợp lệ.");
        }

        private void ApplyAuthorizationHeader(HttpRequestMessage request)
        {
            var authHeader = _httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authHeader))
            {
                if (AuthenticationHeaderValue.TryParse(authHeader, out var parsed))
                {
                    request.Headers.Authorization = parsed;
                }
                else
                {
                    request.Headers.TryAddWithoutValidation("Authorization", authHeader);
                }
            }
        }
    }
}
