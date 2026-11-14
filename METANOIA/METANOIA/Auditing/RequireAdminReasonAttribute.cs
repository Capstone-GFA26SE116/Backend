using METANOIA.Application.Exceptions;
using Microsoft.AspNetCore.Mvc.Filters;

namespace METANOIA.Auditing
{
    public static class AdminReason
    {
        public static bool IsProvided(HttpContext context) =>
            HttpAdminAuditContext.Decode(context.Request.Headers[HttpAdminAuditContext.ReasonHeader].ToString()).Length > 0;

        // Ném UserFriendlyException (400) nếu Admin chưa gửi lý do
        public static void Require(HttpContext context)
        {
            if (!IsProvided(context))
            {
                throw new UserFriendlyException(
                    $"Thao tác quản trị này cần ghi rõ lý do. Hãy gửi header {HttpAdminAuditContext.ReasonHeader} " +
                    "(chữ có dấu thì mã hóa bằng encodeURIComponent, vì header HTTP không chứa được ký tự ngoài Latin-1).");
            }
        }
    }

    // Gắn lên endpoint nhạy cảm của Admin: thiếu header X-Admin-Reason thì 400 và KHÔNG thực hiện gì.
    // Lý do này được ghi vào AdminActionLog cùng với giá trị cũ và mới.
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class RequireAdminReasonAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            AdminReason.Require(context.HttpContext);
        }
    }
}
