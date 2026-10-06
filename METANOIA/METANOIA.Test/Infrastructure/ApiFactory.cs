using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Test.Infrastructure
{
    public class ApiFactory : WebApplicationFactory<Program>
    {
        public const string FrontendOrigin = "http://localhost:5173";
        public const string CalendarRedirectUri = "https://localhost/fe-test.html";

        private readonly string _databaseName = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-only-signing-key-0123456789-abcdefghijklmnop",
                ["Jwt:Issuer"] = "METANOIA",
                ["Jwt:Audience"] = "METANOIA.Client",
                ["Jwt:ExpiryMinutes"] = "60",
                ["Google:ClientId"] = "test-client-id",
                ["Google:ClientSecret"] = "test-client-secret",
                ["Google:CalendarRedirectUri"] = "https://localhost/api/google-calendar/callback",
                ["Frontend:AllowedOrigins:0"] = FrontendOrigin,
                ["Frontend:CalendarConnectedRedirectUri"] = CalendarRedirectUri,
                ["Storage:RootPath"] = Path.Combine(Path.GetTempPath(), "metanoia-tests", _databaseName)
            }));

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));

                services.RemoveAll<IGoogleTokenValidator>();
                services.AddScoped<IGoogleTokenValidator, FakeGoogleTokenValidator>();
            });
        }

        public async Task EnsureDefaultRoleAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (!await db.Roles.AnyAsync(r => r.Name == "Freelancer"))
            {
                db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = "Freelancer" });
                await db.SaveChangesAsync();
            }
        }

        public async Task SeedUserWithRoleAsync(string subject, string roleName)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
            if (role is null)
            {
                role = new Role { Id = Guid.NewGuid(), Name = roleName };
                db.Roles.Add(role);
            }

            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                RoleId = role.Id,
                Email = $"{subject}@test.com",
                FullName = $"Test {subject}",
                GoogleSubjectId = subject,
                TimeZone = "Asia/Ho_Chi_Minh"
            });
            await db.SaveChangesAsync();
        }

        public async Task SeedAsync(Action<AppDbContext> seed)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            seed(db);
            await db.SaveChangesAsync();
        }
    }
}
