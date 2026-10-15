using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace METANOIA.Test.Infrastructure
{
    // Thay máy chủ token của Google (oauth2.googleapis.com) bằng phản hồi do test chỉ định
    public class FakeGoogleHttpHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") };

        public ConcurrentQueue<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue($"{request.Method} {request.RequestUri}");
            return Task.FromResult(Responder(request));
        }

        public static HttpResponseMessage Json(System.Net.HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    }

    public record CapturedLog(LogLevel Level, string Category, string Message, Exception? Exception);

    // Ghi lại mọi dòng log để test kiểm tra "đã LogError"
    public class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<CapturedLog> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly string _category;
            private readonly ConcurrentQueue<CapturedLog> _entries;

            public CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries)
            {
                _category = category;
                _entries = entries;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _entries.Enqueue(new CapturedLog(logLevel, _category, formatter(state, exception), exception));
            }
        }
    }
}
