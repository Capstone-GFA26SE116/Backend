using METANOIA.Application;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Auditing;
using METANOIA.Domain.Configuration;
using METANOIA.Infrastructure;
using METANOIA.Infrastructure.Notifications;
using METANOIA.Infrastructure.Services;
using METANOIA.MiddleWare;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers(options => options.Filters.Add<AdminAuditFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddHttpContextAccessor();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Dán accessToken nhận từ POST /api/auth/google (không cần gõ chữ Bearer)."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Hangfire chạy các việc nền theo slot (nhắc việc, đổi trạng thái Due/Missed/Overrun, tự ngắt phiên quá lâu).
// Job được lưu trong schema "hangfire" của chính database (tự tạo bảng ở lần chạy đầu tiên).
// "Hangfire:Enabled": false thì không dựng Hangfire và không hẹn job nào (dùng khi chạy test, hoặc khi chưa muốn tạo schema trong DB).
// "Hangfire:SchemaName" (mặc định "hangfire"): mọi server cùng schema thì CÙNG chia nhau job. Máy local và server deploy dùng chung
// database mà chung schema thì server deploy (code cũ, cấu hình khác) có thể lấy mất job của bạn; đặt tên khác cho máy local để tách riêng.
// "Hangfire:WorkerCount" (mặc định 2): mỗi worker giữ một kết nối tới Postgres, giữ nhỏ để không vượt hạn mức kết nối của Supabase.
var hangfireEnabled = builder.Configuration.GetValue("Hangfire:Enabled", true);
if (hangfireEnabled)
{
    builder.Services.AddHangfire(configuration => configuration
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(
            options => options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")),
            new PostgreSqlStorageOptions { SchemaName = builder.Configuration["Hangfire:SchemaName"] ?? "hangfire" }));
    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = Math.Max(1, builder.Configuration.GetValue("Hangfire:WorkerCount", 2));
        // Mặc định Hangfire chỉ quét job hẹn giờ mỗi 15 giây nên thông báo có thể trễ tới 15 giây. 5 giây là đủ mịn mà vẫn nhẹ
        options.SchedulePollingInterval = TimeSpan.FromSeconds(5);
    });
}
else
{
    builder.Services.AddScoped<IScheduleJobService, NullScheduleJobService>();
}

// Cho tầng dữ liệu biết khi nào đang xử lý thao tác ghi của Admin để ghi AdminActionLog
builder.Services.AddScoped<IAdminAuditContext, HttpAdminAuditContext>();
// 1. Cấu hình VAPID Settings
builder.Services.Configure<VapidSettings>(
    builder.Configuration.GetSection("VapidSettings"));

// 2. Thêm dịch vụ SignalR
builder.Services.AddSignalR();
var allowedOrigins = builder.Configuration.GetSection("Frontend:AllowedOrigins").Get<string[]>() ?? [];
// AllowCredentials: SignalR (/hubs/notifications) từ trình duyệt ở domain khác gửi kèm thông tin xác thực nên cần bật.
// An toàn vì chỉ cho phép đúng các origin trong Frontend:AllowedOrigins (không dùng AllowAnyOrigin).
builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!))
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrWhiteSpace(accessToken)
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs/notifications"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();

                if (!Guid.TryParse(userIdValue, out var userId)
                    || !await users.IsActiveAsync(userId, context.HttpContext.RequestAborted))
                {
                    context.Fail("Tài khoản không tồn tại hoặc đã bị xóa.");
                }
            }
        };
    });

var app = builder.Build();

// Lệnh chạy một lần cho user cũ: dotnet run --project METANOIA/METANOIA -- --backfill-user-data
// Bù project "Cá nhân", gói Miễn phí và 24 dòng circadian còn thiếu rồi thoát (không khởi động web server).
if (args.Contains(UserBackfillRunner.CommandLineFlag))
{
    var result = await UserBackfillRunner.RunAsync(app.Services);
    Console.WriteLine($"Backfill xong: {result.Total} user cần bù, {result.Initialized} thành công, {result.Failed} lỗi.");
    return result.Failed == 0 ? 0 : 1;
}

// Configure the HTTP request pipeline.
// Swagger mặc định chỉ bật khi chạy Development. Trên server hosting (Production) bật bằng "Swagger:Enabled": true
// trong appsettings.json để test API; tắt đi (false) khi không cần nữa vì nó phơi toàn bộ danh sách API ra internet.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseStaticFiles(); // serves wwwroot/login-test.html, a dev-only helper to obtain a real Google ID token
}

app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseHttpsRedirection();

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
if (hangfireEnabled)
{
    // Mặc định Hangfire chỉ cho truy cập dashboard từ chính máy chạy server (localhost), không mở ra internet
    app.UseHangfireDashboard("/hangfire");
}
app.MapHub<NotificationHub>("/hubs/notifications");
app.Run();

return 0;

public partial class Program
{
}
