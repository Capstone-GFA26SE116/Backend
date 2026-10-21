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
builder.Services.AddHangfire(configuration => configuration
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(
        builder.Configuration.GetConnectionString("DefaultConnection"))));
builder.Services.AddHangfireServer();

// Cho tầng dữ liệu biết khi nào đang xử lý thao tác ghi của Admin để ghi AdminActionLog
builder.Services.AddScoped<IAdminAuditContext, HttpAdminAuditContext>();
// 1. Cấu hình VAPID Settings
builder.Services.Configure<VapidSettings>(
    builder.Configuration.GetSection("VapidSettings"));

// 2. Thêm dịch vụ SignalR
builder.Services.AddSignalR();
var allowedOrigins = builder.Configuration.GetSection("Frontend:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

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
app.UseHangfireDashboard("/hangfire");
app.MapHub<NotificationHub>("/hubs/notifications");
app.Run();

return 0;

public partial class Program
{
}
