using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Google.Apis.Calendar.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test.Infrastructure
{
    public sealed record CalendarApiCall(string Method, string Path, string Query, string Body)
    {
        public JsonNode? Json => string.IsNullOrWhiteSpace(Body) ? null : JsonNode.Parse(Body);

        // Giá trị tham số query đã giải mã; null nếu không có
        public string? Param(string name) =>
            Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => p[0] == name)
                .Select(p => Uri.UnescapeDataString(p.Length > 1 ? p[1] : string.Empty))
                .FirstOrDefault();
    }

    // Google Calendar API giả, đủ để thử đồng bộ hai chiều: danh sách (trả sự kiện do test đặt), thêm (trùng id thì 409),
    // sửa (không có thì 404), xóa (không có thì 410). Ghi lại mọi lời gọi để test kiểm tra tham số
    public sealed class FakeCalendarApiHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<CalendarApiCall> _calls = new();

        // Sự kiện đang có trên "Google", theo id
        public Dictionary<string, JsonObject> Events { get; } = new();

        // Sự kiện trả về cho lời gọi danh sách (nếu null thì trả mọi sự kiện đang có)
        public List<JsonObject>? ListResponse { get; set; }

        public string? NextSyncToken { get; set; }

        // Trả lỗi này cho mọi lời gọi thêm (POST) khi khác null
        public HttpStatusCode? FailInsertWith { get; set; }

        public IReadOnlyList<CalendarApiCall> Calls { get { lock (_gate) { return _calls.ToList(); } } }

        public IEnumerable<CalendarApiCall> Of(string method) => Calls.Where(c => c.Method == method);

        public static JsonObject GoogleEvent(string id, string title, DateTime startUtc, DateTime endUtc, string status = "confirmed") => new()
        {
            ["id"] = id, ["status"] = status, ["summary"] = title, ["etag"] = "\"e1\"",
            ["start"] = new JsonObject { ["dateTime"] = startUtc.ToString("yyyy-MM-ddTHH:mm:ss'Z'") },
            ["end"] = new JsonObject { ["dateTime"] = endUtc.ToString("yyyy-MM-ddTHH:mm:ss'Z'") }
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var call = new CalendarApiCall(request.Method.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, body);
            lock (_gate) { _calls.Add(call); }

            const string eventsPath = "/calendar/v3/calendars/primary/events";
            var tail = call.Path.StartsWith(eventsPath + "/", StringComparison.Ordinal) ? call.Path[(eventsPath.Length + 1)..] : null;
            var id = tail is null ? null : Uri.UnescapeDataString(tail);

            lock (_gate)
            {
                if (call.Method == "GET" && call.Path == eventsPath)
                {
                    var items = new JsonArray();
                    foreach (var e in ListResponse ?? Events.Values.ToList()) items.Add(JsonNode.Parse(e.ToJsonString()));
                    var page = new JsonObject { ["items"] = items };
                    if (NextSyncToken is not null) page["nextSyncToken"] = NextSyncToken;
                    return Json(HttpStatusCode.OK, page);
                }

                if (call.Method == "POST" && call.Path == eventsPath)
                {
                    if (FailInsertWith is { } failure) return Json(failure, new JsonObject { ["error"] = new JsonObject { ["code"] = (int)failure, ["message"] = "fake" } });
                    var created = (JsonObject)call.Json!;
                    var newId = created["id"]!.GetValue<string>();
                    if (Events.ContainsKey(newId)) return Json(HttpStatusCode.Conflict, new JsonObject { ["error"] = new JsonObject { ["code"] = 409, ["message"] = "exists" } });
                    created["etag"] = "\"e1\"";
                    Events[newId] = created;
                    return Json(HttpStatusCode.OK, created);
                }

                if (call.Method == "PUT" && id is not null)
                {
                    if (!Events.ContainsKey(id)) return Json(HttpStatusCode.NotFound, new JsonObject { ["error"] = new JsonObject { ["code"] = 404, ["message"] = "missing" } });
                    var updated = (JsonObject)call.Json!;
                    updated["etag"] = "\"e2\"";
                    Events[id] = updated;
                    return Json(HttpStatusCode.OK, updated);
                }

                if (call.Method == "DELETE" && id is not null)
                {
                    if (!Events.Remove(id)) return Json(HttpStatusCode.Gone, new JsonObject { ["error"] = new JsonObject { ["code"] = 410, ["message"] = "gone" } });
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
            }

            return Json(HttpStatusCode.NotFound, new JsonObject { ["error"] = new JsonObject { ["code"] = 404, ["message"] = "unhandled " + call.Method + " " + call.Path } });
        }

        private static HttpResponseMessage Json(HttpStatusCode status, JsonNode node) =>
            new(status) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    // Mở phiên Google Calendar nhưng dùng máy chủ giả thay vì googleapis.com
    public sealed class FakeGoogleCalendarClientProvider : GoogleCalendarClientProvider
    {
        private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : HttpClientFactory
        {
            protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => handler;
        }

        private readonly IGoogleConnectionRepository _connections;
        private readonly FakeCalendarApiHandler _api;

        public FakeGoogleCalendarClientProvider(
            IGoogleConnectionRepository connections, IUnitOfWork unitOfWork, GoogleOAuthClient oauth,
            IDataProtectionProvider dataProtection, FakeCalendarApiHandler api)
            : base(connections, unitOfWork, oauth, dataProtection)
        {
            _connections = connections;
            _api = api;
        }

        public override async Task<GoogleCalendarSession> OpenAsync(Guid userId, CancellationToken cancellationToken)
        {
            var connection = await _connections.GetByUserIdAsync(userId, cancellationToken);
            if (connection is null || connection.Status != "Active")
            {
                throw new GoogleCalendarNotConnectedException();
            }

            var service = new CalendarService(new BaseClientService.Initializer
            {
                HttpClientFactory = new FakeHttpClientFactory(_api),
                GZipEnabled = false,   // để máy chủ giả đọc được nội dung gửi lên
                ApplicationName = "METANOIA.Test"
            });
            return new GoogleCalendarSession(connection, service);
        }
    }
}
