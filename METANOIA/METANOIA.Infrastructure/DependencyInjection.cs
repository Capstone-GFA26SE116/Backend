using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Infrastructure.Data;
using METANOIA.Infrastructure.Repositories;
using METANOIA.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace METANOIA.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            // Mặc định không ghi AdminActionLog; web project thay bằng bản đọc HttpContext
            services.TryAddScoped<IAdminAuditContext, NullAdminAuditContext>();
            services.AddScoped<AdminAuditSaveChangesInterceptor>();
            services.AddDbContext<AppDbContext>((serviceProvider, options) =>
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"))
                    .AddInterceptors(serviceProvider.GetRequiredService<AdminAuditSaveChangesInterceptor>()));

            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.Configure<SubscriptionOptions>(configuration.GetSection("Subscription"));
            services.AddScoped<IUserSetupRepository, UserSetupRepository>();
            services.AddScoped<IUserInitializationService, UserInitializationService>();
            services.AddScoped<IMeService, MeService>();
            services.AddScoped<IAdminUserRepository, AdminUserRepository>();
            services.AddScoped<IAdminUserService, AdminUserService>();
            services.AddScoped<IAiUsageRepository, AiUsageRepository>();
            services.AddScoped<IAiUsageReportService, AiUsageReportService>();
            services.AddScoped<IFocusTrackRepository, FocusTrackRepository>();
            services.AddScoped<IFocusTrackService, FocusTrackService>();
            services.AddScoped<IAdminAuditLogRepository, AdminAuditLogRepository>();
            services.AddScoped<IAdminAuditLogService, AdminAuditLogService>();
            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
            services.AddSingleton<GoogleSyncGate>();
            services.AddScoped<IGoogleCalendarWebhookService, GoogleCalendarWebhookService>();
            services.AddScoped<ICalendarRepository, CalendarRepository>();
            services.AddScoped<ICalendarService, CalendarViewService>();
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
            services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();
            services.AddScoped<IPaymentTransactionService, PaymentTransactionService>();
            services.AddScoped<IPaymentRefundRepository, PaymentRefundRepository>();
            services.AddScoped<IPaymentService, PaymentService>();
            services.AddScoped<IRefundService, RefundService>();
            services.Configure<PayOSOptions>(configuration.GetSection("PayOS"));
            services.Configure<PaymentOptions>(configuration.GetSection("Payments"));
            services.AddHttpClient<IPayOSClient, PayOSClient>(client => client.Timeout = TimeSpan.FromSeconds(15));
            services.AddHostedService<PaymentReconciliationWorker>();
            services.AddScoped<IInvoiceRepository, InvoiceRepository>();
            services.AddScoped<IInvoiceService, InvoiceService>();
            services.Configure<AiServiceOptions>(configuration.GetSection("AiService"));
            services.AddHttpClient<IAiServiceClient, AiServiceClient>();
            services.AddScoped<IScheduleProposalService, ScheduleProposalService>();
            services.AddHttpClient();

            return services;
        }
    }
}
