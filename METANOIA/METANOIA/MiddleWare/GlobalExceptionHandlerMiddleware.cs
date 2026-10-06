using System.Net;
using System.Text.Json;
using METANOIA.Application.Exceptions;

namespace METANOIA.MiddleWare
{
    public class GlobalExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;
        private readonly IHostEnvironment _environment;

        public GlobalExceptionHandlerMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionHandlerMiddleware> logger,
            IHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (UserFriendlyException ex)
            {
                _logger.LogWarning(ex, "A user-friendly exception occurred");
                await WriteResponseAsync(context, HttpStatusCode.BadRequest, new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                _logger.LogWarning(ex, "Resource not found");
                await WriteResponseAsync(context, HttpStatusCode.NotFound, new { message = ex.Message });
            }
            catch (GoogleCalendarNotConnectedException ex)
            {
                _logger.LogWarning(ex, "Google Calendar is not connected");
                await WriteResponseAsync(context, HttpStatusCode.Conflict, new
                {
                    message = ex.Message,
                    code = "GOOGLE_CALENDAR_NOT_CONNECTED",
                    connectUrl = "/api/google-calendar/connect"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception occurred");
                await WriteResponseAsync(context, HttpStatusCode.InternalServerError, new
                {
                    message = "An unexpected error occurred. Please try again later.",
                    detail = _environment.IsDevelopment() ? $"{ex.GetType().Name}: {ex.Message}" : null
                });
            }
        }

        private static Task WriteResponseAsync(HttpContext context, HttpStatusCode statusCode, object payload)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)statusCode;

            return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
        }
    }
}
