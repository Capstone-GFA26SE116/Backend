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
            catch (InvalidRefreshTokenException ex)
            {
                _logger.LogWarning(ex, "Invalid refresh token");
                await WriteResponseAsync(context, HttpStatusCode.Unauthorized, new
                {
                    message = ex.Message,
                    code = "INVALID_REFRESH_TOKEN"
                });
            }
            catch (NotFoundException ex)
            {
                _logger.LogWarning(ex, "Resource not found");
                await WriteResponseAsync(context, HttpStatusCode.NotFound, new { message = ex.Message });
            }
            catch (FocusConflictException ex)
            {
                _logger.LogWarning(ex, "Focus session conflict");
                await WriteResponseAsync(context, HttpStatusCode.Conflict, new
                {
                    message = ex.Message,
                    code = "FOCUS_SESSION_CONFLICT",
                    details = ex.Details
                });
            }
            catch (FocusNotFoundException ex)
            {
                _logger.LogWarning(ex, "Focus session not found");
                await WriteResponseAsync(context, HttpStatusCode.NotFound, new { message = ex.Message });
            }
            catch (FocusValidationException ex)
            {
                _logger.LogWarning(ex, "Focus session validation failed");
                await WriteResponseAsync(context, HttpStatusCode.BadRequest, new { message = ex.Message });
            }
            catch (FocusForbiddenException ex)
            {
                _logger.LogWarning(ex, "User is not allowed to access the focus session");
                await WriteResponseAsync(context, HttpStatusCode.Forbidden, new
                {
                    message = ex.Message,
                    code = "FOCUS_SESSION_FORBIDDEN"
                });
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
            catch (GoogleReconnectRequiredException ex)
            {
                _logger.LogWarning(ex, "Google Calendar needs to be reconnected");
                await WriteResponseAsync(context, HttpStatusCode.Conflict, new
                {
                    message = ex.Message,
                    code = "GOOGLE_RECONNECT_REQUIRED",
                    connectUrl = "/api/google-calendar/connect"
                });
            }
            catch (GoogleApiFailureException ex)
            {
                _logger.LogWarning(ex, "Google API failure");
                await WriteResponseAsync(context, HttpStatusCode.BadGateway, new
                {
                    message = "Không đồng bộ được với Google Calendar lúc này. Vui lòng thử lại sau.",
                    code = "GOOGLE_API_ERROR"
                });
            }
            catch (PaymentGatewayException ex)
            {
                _logger.LogWarning(ex, "Payment gateway unavailable");
                await WriteResponseAsync(context, HttpStatusCode.ServiceUnavailable, new
                {
                    message = ex.Message,
                    code = "PAYMENT_GATEWAY_UNAVAILABLE"
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

            return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
        }

        // camelCase như mọi API khác. Cần cho thân lỗi chứa object lồng nhau (ví dụ 409 của focus session trả kèm phiên đang chạy)
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    }
}
