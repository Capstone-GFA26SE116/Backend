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
            services.AddScoped<GoogleCalendarClientProvider>();
            services.AddScoped<IGoogleCalendarEventRepository, GoogleCalendarEventRepository>();
            services.AddScoped<IGoogleCalendarEventService, GoogleCalendarEventService>();
            services.AddScoped<IGoogleCalendarSyncService, GoogleCalendarSyncService>();
            services.AddScoped<GoogleOAuthClient>();
            services.AddScoped<IProjectRepository, ProjectRepository>();
            services.AddScoped<IProjectService, ProjectService>();
            services.AddScoped<IClientRepository, ClientRepository>();
            services.AddScoped<IClientService, ClientService>();
            services.AddScoped<IDomainRepository, DomainRepository>();
            services.AddScoped<IDomainService, DomainService>();
            services.AddScoped<IMilestoneRepository, MilestoneRepository>();
            services.AddScoped<IMilestoneService, MilestoneService>();
            services.AddScoped<ITaskRepository, TaskRepository>();
            services.AddScoped<ITaskService, TaskService>();
            services.AddScoped<ISubTaskRepository, SubTaskRepository>();
            services.AddScoped<ISubTaskService, SubTaskService>();
            services.AddScoped<IScheduleSlotRepository, ScheduleSlotRepository>();
            services.AddScoped<IScheduleSlotService, ScheduleSlotService>();
            services.AddScoped<IScheduleChangeLogRepository, ScheduleChangeLogRepository>();
            services.AddScoped<IScheduleChangeLogService, ScheduleChangeLogService>();
            services.AddScoped<IProjectDocumentRepository, ProjectDocumentRepository>();
            services.AddScoped<IProjectDocumentService, ProjectDocumentService>();
            services.AddScoped<IFileStorage, LocalFileStorage>();
            services.AddScoped<ISubscriptionPlanRepository, SubscriptionPlanRepository>();
            services.AddScoped<ISubscriptionPlanService, SubscriptionPlanService>();
            services.AddScoped<IUserSubscriptionRepository, UserSubscriptionRepository>();
            services.AddScoped<IUserSubscriptionService, UserSubscriptionService>();
            services.AddScoped<IInvoiceRepository, InvoiceRepository>();
            services.AddScoped<IInvoiceService, InvoiceService>();
            services.AddHttpClient();

            return services;
        }
    }
}
