using System.Security.Claims;
using METANOIA.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace METANOIA.Auditing
{
    // Đọc HttpContext để cho tầng dữ liệu biết có đang xử lý thao tác ghi của Admin hay không
    public class HttpAdminAuditContext : IAdminAuditContext
    {
        public const string ItemKey = "METANOIA.AdminAudit";
        public const string ReasonHeader = "X-Admin-Reason";
        public const string DefaultReason = "Không ghi lý do";

        private readonly IHttpContextAccessor _accessor;

        public HttpAdminAuditContext(IHttpContextAccessor accessor)
        {
            _accessor = accessor;
        }

        public bool IsActive => _accessor.HttpContext?.Items[ItemKey] is true && AdminId is not null;

        public Guid? AdminId =>
            Guid.TryParse(_accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        public string Reason
        {
            get
            {
                var header = _accessor.HttpContext?.Request.Headers[ReasonHeader].ToString().Trim();
                return string.IsNullOrEmpty(header) ? DefaultReason : header;
            }
        }
    }

    // Đánh dấu request là "thao tác ghi của Admin" khi endpoint yêu cầu role Admin và không phải GET/HEAD/OPTIONS.
    // Nhờ đó mọi endpoint quản trị hiện có và sau này đều được ghi AdminActionLog mà không phải sửa từng service.
    public class AdminAuditFilter : IActionFilter
    {
        private const string AdminRole = "Admin";

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var method = context.HttpContext.Request.Method;
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            {
                return;
            }

            var adminOnly = context.ActionDescriptor.EndpointMetadata
                .OfType<IAuthorizeData>()
                .Any(data => data.Roles is not null
                    && data.Roles.Split(',').Select(role => role.Trim()).Contains(AdminRole));

            if (adminOnly)
            {
                context.HttpContext.Items[HttpAdminAuditContext.ItemKey] = true;
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
