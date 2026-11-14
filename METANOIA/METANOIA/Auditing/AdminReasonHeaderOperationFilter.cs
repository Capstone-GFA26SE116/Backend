using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace METANOIA.Auditing
{
    // Hiện ô nhập header X-Admin-Reason trên Swagger cho các API ghi của Admin (nếu không khai báo thì Swagger không có chỗ để gửi header này).
    //  - Có [RequireAdminReason]: ô BẮT BUỘC.
    //  - Còn lại (Admin, không phải GET): ô tùy chọn.
    public class AdminReasonHeaderOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var method = context.ApiDescription.HttpMethod;
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
            var adminOnly = metadata.OfType<IAuthorizeData>()
                .Any(data => data.Roles is not null && data.Roles.Split(',').Select(r => r.Trim()).Contains("Admin"));
            if (!adminOnly)
            {
                return;
            }

            var required = metadata.OfType<RequireAdminReasonAttribute>().Any();
            operation.Parameters ??= new List<OpenApiParameter>();
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = HttpAdminAuditContext.ReasonHeader,
                In = ParameterLocation.Header,
                Required = required,
                Description = required
                    ? "Lý do thực hiện thao tác (bắt buộc), ghi vào nhật ký Admin. Header HTTP không nhận chữ có dấu: gõ không dấu, hoặc mã hóa kiểu URL (ví dụ thay%20doi%20gia)."
                    : "Lý do thực hiện (tùy chọn, ghi vào nhật ký Admin). Bắt buộc khi đổi role người dùng hoặc chạy thật status-resync (dryRun=false). Gõ không dấu hoặc mã hóa kiểu URL.",
                Schema = new OpenApiSchema { Type = "string" }
            });
        }
    }
}
