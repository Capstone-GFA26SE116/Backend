using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Infrastructure.Data;
using METANOIA.Infrastructure.Repositories;
using METANOIA.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace METANOIA.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IUserRepository, AuthRepository>();
            services.AddScoped<IUserService, AuthService>();
            services.AddScoped<IGoogleTokenValidator, GoogleTokenValidator>();
            services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
            services.AddScoped<IGoogleConnectionRepository, GoogleConnectionRepository>();
            services.AddScoped<IGoogleCalendarConnectionService, GoogleCalendarConnectionService>();
            services.AddScoped<IGoogleCalendarEventService, GoogleCalendarEventService>();
            services.AddScoped<GoogleOAuthClient>();
            services.AddScoped<IProjectRepository, ProjectRepository>();
            services.AddScoped<IProjectService, ProjectService>();
            services.AddHttpClient();

            return services;
        }
    }
}
