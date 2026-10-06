using METANOIA.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using Task = METANOIA.Domain.Entities.Task;

namespace METANOIA.Infrastructure.Data;

public partial class AppDbContext : DbContext
{
    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AdminActionLog> AdminActionLogs { get; set; }

    public virtual DbSet<AiusageLog> AiusageLogs { get; set; }

    public virtual DbSet<BlindTestSession> BlindTestSessions { get; set; }

    public virtual DbSet<ChatConversation> ChatConversations { get; set; }

    public virtual DbSet<ChatMessage> ChatMessages { get; set; }

    public virtual DbSet<CircadianUpdateLog> CircadianUpdateLogs { get; set; }

    public virtual DbSet<Client> Clients { get; set; }

    public virtual DbSet<METANOIA.Domain.Entities.Domain> Domains { get; set; }

    public virtual DbSet<FocusSession> FocusSessions { get; set; }

    public virtual DbSet<FocusTrack> FocusTracks { get; set; }

    public virtual DbSet<GoogleCalendarEvent> GoogleCalendarEvents { get; set; }

    public virtual DbSet<GoogleConnection> GoogleConnections { get; set; }

    public virtual DbSet<Invoice> Invoices { get; set; }

    public virtual DbSet<KbiasUpdateLog> KbiasUpdateLogs { get; set; }

    public virtual DbSet<Milestone> Milestones { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<PaymentTransaction> PaymentTransactions { get; set; }

    public virtual DbSet<Project> Projects { get; set; }

    public virtual DbSet<ProjectDocument> ProjectDocuments { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<ScheduleChangeLog> ScheduleChangeLogs { get; set; }

    public virtual DbSet<ScheduleProposal> ScheduleProposals { get; set; }

    public virtual DbSet<ScheduleSlot> ScheduleSlots { get; set; }

    public virtual DbSet<SubTask> SubTasks { get; set; }

    public virtual DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }

    public virtual DbSet<Task> Tasks { get; set; }

    public virtual DbSet<TaskExecutionMemory> TaskExecutionMemories { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserCircadianProfile> UserCircadianProfiles { get; set; }

    public virtual DbSet<UserDomainProfile> UserDomainProfiles { get; set; }

    public virtual DbSet<UserSubscription> UserSubscriptions { get; set; }

    private string GetConnectionString()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true, true).Build();
        return configuration["ConnectionStrings:DefaultConnection"];
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql(GetConnectionString());
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasPostgresEnum("auth", "aal_level", new[] { "aal1", "aal2", "aal3" })
            .HasPostgresEnum("auth", "code_challenge_method", new[] { "s256", "plain" })
            .HasPostgresEnum("auth", "factor_status", new[] { "unverified", "verified" })
            .HasPostgresEnum("auth", "factor_type", new[] { "totp", "webauthn", "phone", "recovery_code" })
            .HasPostgresEnum("auth", "oauth_authorization_status", new[] { "pending", "approved", "denied", "expired" })
            .HasPostgresEnum("auth", "oauth_client_type", new[] { "public", "confidential" })
            .HasPostgresEnum("auth", "oauth_registration_type", new[] { "dynamic", "manual" })
            .HasPostgresEnum("auth", "oauth_response_type", new[] { "code" })
            .HasPostgresEnum("auth", "one_time_token_type", new[] { "confirmation_token", "reauthentication_token", "recovery_token", "email_change_token_new", "email_change_token_current", "phone_change_token" })
            .HasPostgresEnum("realtime", "action", new[] { "INSERT", "UPDATE", "DELETE", "TRUNCATE", "ERROR" })
            .HasPostgresEnum("realtime", "equality_op", new[] { "eq", "neq", "lt", "lte", "gt", "gte", "in", "like", "ilike", "is", "match", "imatch", "isdistinct" })
            .HasPostgresEnum("storage", "buckettype", new[] { "STANDARD", "ANALYTICS", "VECTOR" })
            .HasPostgresExtension("extensions", "pg_stat_statements")
            .HasPostgresExtension("extensions", "pgcrypto")
            .HasPostgresExtension("extensions", "uuid-ossp")
            .HasPostgresExtension("pg_trgm")
            .HasPostgresExtension("vector")
            .HasPostgresExtension("vault", "supabase_vault");

        modelBuilder.Entity<AdminActionLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("AdminActionLog_pkey");

            entity.ToTable("AdminActionLog");

            entity.HasIndex(e => new { e.AdminId, e.CreatedAt }, "IX_AdminActionLog_AdminId_CreatedAt");

            entity.HasIndex(e => new { e.TargetType, e.TargetId }, "IX_AdminActionLog_Target");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Action).HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.NewValue).HasColumnType("jsonb");
            entity.Property(e => e.OldValue).HasColumnType("jsonb");
            entity.Property(e => e.TargetType).HasMaxLength(50);

            entity.HasOne(d => d.Admin).WithMany(p => p.AdminActionLogs)
                .HasForeignKey(d => d.AdminId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("AdminActionLog_AdminId_fkey");
        });

        modelBuilder.Entity<AiusageLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("AIUsageLog_pkey");

            entity.ToTable("AIUsageLog");

            entity.HasIndex(e => new { e.Feature, e.CreatedAt }, "IX_AIUsageLog_Feature_CreatedAt");

            entity.HasIndex(e => new { e.UserSubscriptionId, e.CreatedAt }, "IX_AIUsageLog_UserSubscriptionId_CreatedAt");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Cost).HasPrecision(10, 6);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Feature).HasMaxLength(20);
            entity.Property(e => e.InputTokens).HasDefaultValue(0);
            entity.Property(e => e.Model).HasMaxLength(50);
            entity.Property(e => e.OutputTokens).HasDefaultValue(0);
            entity.Property(e => e.QuotaCharged).HasDefaultValue(false);

            entity.HasOne(d => d.UserSubscription).WithMany(p => p.AiusageLogs)
                .HasForeignKey(d => d.UserSubscriptionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("AIUsageLog_UserSubscriptionId_fkey");
        });

        modelBuilder.Entity<BlindTestSession>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("BlindTestSession_pkey");

            entity.ToTable("BlindTestSession");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Status)
                .HasMaxLength(10)
                .HasDefaultValueSql("'Pending'::character varying");
        });

        modelBuilder.Entity<ChatConversation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("ChatConversation_pkey");

            entity.ToTable("ChatConversation");

            entity.HasIndex(e => new { e.ProjectId, e.CreatedAt }, "IX_ChatConversation_ProjectId_CreatedAt");

            entity.HasIndex(e => e.TaskId, "IX_ChatConversation_TaskId");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Scope).HasMaxLength(10);
            entity.Property(e => e.Status)
                .HasMaxLength(10)
                .HasDefaultValueSql("'Open'::character varying");
            entity.Property(e => e.Title).HasMaxLength(200);

            entity.HasOne(d => d.Project).WithMany(p => p.ChatConversations)
                .HasForeignKey(d => d.ProjectId)
                .HasConstraintName("ChatConversation_ProjectId_fkey");

            entity.HasOne(d => d.Task).WithMany(p => p.ChatConversations)
                .HasForeignKey(d => d.TaskId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ChatConversation_TaskId_fkey");
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("ChatMessage_pkey");

            entity.ToTable("ChatMessage");

            entity.HasIndex(e => new { e.ConversationId, e.CreatedAt }, "IX_ChatMessage_ConversationId_CreatedAt");

            entity.HasIndex(e => e.ConversationId, "UX_ChatMessage_OneAppliedPerConversation")
                .IsUnique()
                .HasFilter("(\"IsApplied\" = true)");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.IsApplied).HasDefaultValue(false);
            entity.Property(e => e.ProposalJson).HasColumnType("jsonb");
            entity.Property(e => e.Sender).HasMaxLength(10);

            entity.HasOne(d => d.Conversation).WithOne(p => p.ChatMessage)
                .HasForeignKey<ChatMessage>(d => d.ConversationId)
                .HasConstraintName("ChatMessage_ConversationId_fkey");
        });

        modelBuilder.Entity<CircadianUpdateLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("CircadianUpdateLog_pkey");

            entity.ToTable("CircadianUpdateLog");

            entity.HasIndex(e => new { e.UserCircadianProfileId, e.CreatedAt }, "IX_CircadianUpdateLog_Profile_CreatedAt");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.NewScore).HasPrecision(4, 3);
            entity.Property(e => e.OldScore).HasPrecision(4, 3);

            entity.HasOne(d => d.UserCircadianProfile).WithMany(p => p.CircadianUpdateLogs)
                .HasForeignKey(d => d.UserCircadianProfileId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("CircadianUpdateLog_UserCircadianProfileId_fkey");
        });

        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Client_pkey");

            entity.ToTable("Client");

            entity.HasIndex(e => e.UserId, "IX_Client_UserId");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.ContactEmail).HasMaxLength(255);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Phone).HasMaxLength(20);

            entity.HasOne(d => d.User).WithMany(p => p.Clients)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("Client_UserId_fkey");
        });

        modelBuilder.Entity<METANOIA.Domain.Entities.Domain>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Domain_pkey");

            entity.ToTable("Domain");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(50);
        });

        modelBuilder.Entity<FocusSession>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("FocusSession_pkey");

            entity.ToTable("FocusSession");

            entity.HasIndex(e => e.ScheduleSlotId, "IX_FocusSession_ScheduleSlotId");

            entity.HasIndex(e => e.StartedAt, "IX_FocusSession_StartedAt");

            entity.HasIndex(e => e.TaskId, "IX_FocusSession_TaskId");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CaptureMethod)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Timer'::character varying");
            entity.Property(e => e.IsConfirmed).HasDefaultValue(true);
            entity.Property(e => e.StartedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.StopReason).HasMaxLength(20);

            entity.HasOne(d => d.FocusTrack).WithMany(p => p.FocusSessions)
                .HasForeignKey(d => d.FocusTrackId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FocusSession_FocusTrackId_fkey");

            entity.HasOne(d => d.ScheduleSlot).WithMany(p => p.FocusSessions)
                .HasForeignKey(d => d.ScheduleSlotId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FocusSession_ScheduleSlotId_fkey");

            entity.HasOne(d => d.SubTask).WithMany(p => p.FocusSessions)
                .HasForeignKey(d => d.SubTaskId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FocusSession_SubTaskId_fkey");

            entity.HasOne(d => d.Task).WithMany(p => p.FocusSessions)
                .HasForeignKey(d => d.TaskId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FocusSession_TaskId_fkey");
        });

        modelBuilder.Entity<FocusTrack>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("FocusTrack_pkey");

            entity.ToTable("FocusTrack");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Artist).HasMaxLength(100);
            entity.Property(e => e.FileUrl).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Title).HasMaxLength(150);
        });

        modelBuilder.Entity<GoogleCalendarEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("GoogleCalendarEvent_pkey");

            entity.ToTable("GoogleCalendarEvent");

            entity.HasIndex(e => new { e.GoogleConnectionId, e.StartTime, e.EndTime }, "IX_GoogleCalendarEvent_Busy").HasFilter("(\"IsCancelled\" = false)");

            entity.HasIndex(e => new { e.GoogleConnectionId, e.GoogleEventId }, "UQ_GoogleCalendarEvent_Conn_Event").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Etag).HasMaxLength(100);
            entity.Property(e => e.GoogleEventId).HasMaxLength(1024);
            entity.Property(e => e.IsAllDay).HasDefaultValue(false);
            entity.Property(e => e.IsCancelled).HasDefaultValue(false);
            entity.Property(e => e.IsCreatedByMetanoia).HasDefaultValue(false);
            entity.Property(e => e.Title)
                .HasMaxLength(500)
                .HasDefaultValueSql("''::character varying");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.GoogleConnection).WithMany(p => p.GoogleCalendarEvents)
                .HasForeignKey(d => d.GoogleConnectionId)
                .HasConstraintName("GoogleCalendarEvent_GoogleConnectionId_fkey");
        });

        modelBuilder.Entity<GoogleConnection>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("GoogleConnection_pkey");

            entity.ToTable("GoogleConnection");

            entity.HasIndex(e => e.UserId, "GoogleConnection_UserId_key").IsUnique();

            entity.HasIndex(e => e.WatchExpiresAt, "IX_GoogleConnection_WatchExpiresAt");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Status)
                .HasMaxLength(10)
                .HasDefaultValueSql("'Active'::character varying");
            entity.Property(e => e.SyncToken).HasMaxLength(500);
            entity.Property(e => e.WatchChannelId).HasMaxLength(100);

            entity.HasOne(d => d.User).WithOne(p => p.GoogleConnection)
                .HasForeignKey<GoogleConnection>(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("GoogleConnection_UserId_fkey");
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Invoice_pkey");

            entity.ToTable("Invoice");

            entity.HasIndex(e => e.InvoiceNumber, "Invoice_InvoiceNumber_key").IsUnique();

            entity.HasIndex(e => e.PaymentTransactionId, "Invoice_PaymentTransactionId_key").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.BillingEmail).HasMaxLength(255);
            entity.Property(e => e.BillingName).HasMaxLength(100);
            entity.Property(e => e.InvoiceNumber).HasMaxLength(30);
            entity.Property(e => e.IssuedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.PlanName).HasMaxLength(50);

            entity.HasOne(d => d.PaymentTransaction).WithOne(p => p.Invoice)
                .HasForeignKey<Invoice>(d => d.PaymentTransactionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("Invoice_PaymentTransactionId_fkey");
        });

        modelBuilder.Entity<KbiasUpdateLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("KBiasUpdateLog_pkey");

            entity.ToTable("KBiasUpdateLog");

            entity.HasIndex(e => e.TaskId, "KBiasUpdateLog_TaskId_key").IsUnique();

            entity.HasIndex(e => new { e.UserDomainProfileId, e.SampleIndex }, "UQ_KBiasUpdateLog_Profile_Sample").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Alpha).HasPrecision(3, 2);
            entity.Property(e => e.CaptureMethod).HasMaxLength(20);
            entity.Property(e => e.ConfidenceTier).HasMaxLength(10);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.NewKbias)
                .HasPrecision(5, 3)
                .HasColumnName("NewKBias");
            entity.Property(e => e.ObservedRatio).HasPrecision(6, 3);
            entity.Property(e => e.OldKbias)
                .HasPrecision(5, 3)
                .HasColumnName("OldKBias");

            entity.HasOne(d => d.Task).WithOne(p => p.KbiasUpdateLog)
                .HasForeignKey<KbiasUpdateLog>(d => d.TaskId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("KBiasUpdateLog_TaskId_fkey");

            entity.HasOne(d => d.UserDomainProfile).WithMany(p => p.KbiasUpdateLogs)
                .HasForeignKey(d => d.UserDomainProfileId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("KBiasUpdateLog_UserDomainProfileId_fkey");
        });

        modelBuilder.Entity<Milestone>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Milestone_pkey");

            entity.ToTable("Milestone");

            entity.HasIndex(e => new { e.ProjectId, e.OrderIndex }, "UQ_Milestone_ProjectId_OrderIndex").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.RevisionCount).HasDefaultValue(0);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Planned'::character varying");

            entity.HasOne(d => d.Project).WithMany(p => p.Milestones)
                .HasForeignKey(d => d.ProjectId)
                .HasConstraintName("Milestone_ProjectId_fkey");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Notification_pkey");

            entity.ToTable("Notification");

            entity.HasIndex(e => e.ScheduledAt, "IX_Notification_Unsent").HasFilter("(\"SentAt\" IS NULL)");

            entity.HasIndex(e => new { e.UserId, e.IsRead, e.ScheduledAt }, "IX_Notification_UserId_IsRead_ScheduledAt");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.IsRead).HasDefaultValue(false);
            entity.Property(e => e.Title).HasMaxLength(150);
            entity.Property(e => e.Type).HasMaxLength(20);

            entity.HasOne(d => d.ScheduleSlot).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.ScheduleSlotId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("Notification_ScheduleSlotId_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("Notification_UserId_fkey");
        });

        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PaymentTransaction_pkey");

            entity.ToTable("PaymentTransaction");

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "IX_PaymentTransaction_UserId_CreatedAt");

            entity.HasIndex(e => e.OrderCode, "PaymentTransaction_OrderCode_key").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Gateway).HasMaxLength(10);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Pending'::character varying");

            entity.HasOne(d => d.Plan).WithMany(p => p.PaymentTransactions)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("PaymentTransaction_PlanId_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.PaymentTransactions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("PaymentTransaction_UserId_fkey");
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Project_pkey");

            entity.ToTable("Project");

            entity.HasIndex(e => new { e.UserId, e.Status }, "IX_Project_UserId_Status");

            entity.HasIndex(e => e.UserId, "UX_Project_OneDefaultPerUser")
                .IsUnique()
                .HasFilter("(\"IsDefault\" = true)");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.IsDefault).HasDefaultValue(false);
            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Active'::character varying");

            entity.HasOne(d => d.Client).WithMany(p => p.Projects)
                .HasForeignKey(d => d.ClientId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("Project_ClientId_fkey");

            entity.HasOne(d => d.User).WithOne(p => p.Project)
                .HasForeignKey<Project>(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("Project_UserId_fkey");
        });

        modelBuilder.Entity<ProjectDocument>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("ProjectDocument_pkey");

            entity.ToTable("ProjectDocument");

            entity.HasIndex(e => e.Name, "IX_ProjectDocument_Name_Trgm")
                .HasMethod("gin")
                .HasOperators(new[] { "gin_trgm_ops" });

            entity.HasIndex(e => e.ProjectId, "IX_ProjectDocument_ProjectId");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.LinkUrl).HasMaxLength(2000);
            entity.Property(e => e.Name).HasMaxLength(255);
            entity.Property(e => e.SourceType).HasMaxLength(10);
            entity.Property(e => e.StoragePath).HasMaxLength(500);
            entity.Property(e => e.Tag)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Other'::character varying");
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Project).WithMany(p => p.ProjectDocuments)
                .HasForeignKey(d => d.ProjectId)
                .HasConstraintName("ProjectDocument_ProjectId_fkey");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Role_pkey");

            entity.ToTable("Role");

            entity.HasIndex(e => e.Name, "Role_Name_key").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).HasMaxLength(20);
        });

        modelBuilder.Entity<ScheduleChangeLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("ScheduleChangeLog_pkey");

            entity.ToTable("ScheduleChangeLog");

            entity.HasIndex(e => new { e.ScheduleSlotId, e.ChangedAt }, "IX_ScheduleChangeLog_Slot_ChangedAt");

            entity.HasIndex(e => new { e.Source, e.ChangedAt }, "IX_ScheduleChangeLog_Source_ChangedAt");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.ChangeType).HasMaxLength(20);
            entity.Property(e => e.ChangedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Source).HasMaxLength(20);

            entity.HasOne(d => d.ScheduleSlot).WithMany(p => p.ScheduleChangeLogs)
                .HasForeignKey(d => d.ScheduleSlotId)
                .HasConstraintName("ScheduleChangeLog_ScheduleSlotId_fkey");
        });

        modelBuilder.Entity<ScheduleProposal>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("ScheduleProposal_pkey");

            entity.ToTable("ScheduleProposal");

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "IX_ScheduleProposal_UserId_CreatedAt");

            entity.HasIndex(e => new { e.BlindTestSessionId, e.Level }, "UQ_ScheduleProposal_Blind_Level").IsUnique();

            entity.HasIndex(e => new { e.BlindTestSessionId, e.DisplayPosition }, "UQ_ScheduleProposal_Blind_Position").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.IsFeasible).HasDefaultValue(true);
            entity.Property(e => e.Level).HasDefaultValue((short)3);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Pending'::character varying");

            entity.HasOne(d => d.BlindTestSession).WithMany(p => p.ScheduleProposals)
                .HasForeignKey(d => d.BlindTestSessionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("ScheduleProposal_BlindTestSessionId_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.ScheduleProposals)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("ScheduleProposal_UserId_fkey");
        });

        modelBuilder.Entity<ScheduleSlot>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("ScheduleSlot_pkey");

            entity.ToTable("ScheduleSlot");

            entity.HasIndex(e => e.ScheduleProposalId, "IX_ScheduleSlot_ScheduleProposalId");

            entity.HasIndex(e => new { e.StartTime, e.EndTime }, "IX_ScheduleSlot_StartTime_EndTime");

            entity.HasIndex(e => e.Status, "IX_ScheduleSlot_SyncFailed").HasFilter("(\"SyncFailed\" = true)");

            entity.HasIndex(e => e.TaskId, "IX_ScheduleSlot_TaskId");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Explanation).HasMaxLength(500);
            entity.Property(e => e.GoogleEventId).HasMaxLength(1024);
            entity.Property(e => e.IsManualOverride).HasDefaultValue(false);
            entity.Property(e => e.KbiasUsed)
                .HasPrecision(5, 3)
                .HasColumnName("KBiasUsed");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Proposed'::character varying");
            entity.Property(e => e.SyncFailed).HasDefaultValue(false);

            entity.HasOne(d => d.ScheduleProposal).WithMany(p => p.ScheduleSlots)
                .HasForeignKey(d => d.ScheduleProposalId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("ScheduleSlot_ScheduleProposalId_fkey");

            entity.HasOne(d => d.SubTask).WithMany(p => p.ScheduleSlots)
                .HasForeignKey(d => d.SubTaskId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ScheduleSlot_SubTaskId_fkey");

            entity.HasOne(d => d.Task).WithMany(p => p.ScheduleSlots)
                .HasForeignKey(d => d.TaskId)
                .HasConstraintName("ScheduleSlot_TaskId_fkey");
        });

        modelBuilder.Entity<SubTask>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("SubTask_pkey");

            entity.ToTable("SubTask");

            entity.HasIndex(e => new { e.TaskId, e.OrderIndex }, "UQ_SubTask_TaskId_OrderIndex").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Status)
                .HasMaxLength(10)
                .HasDefaultValueSql("'ToDo'::character varying");
            entity.Property(e => e.Title).HasMaxLength(200);

            entity.HasOne(d => d.Task).WithMany(p => p.SubTasks)
                .HasForeignKey(d => d.TaskId)
                .HasConstraintName("SubTask_TaskId_fkey");
        });

        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("SubscriptionPlan_pkey");

            entity.ToTable("SubscriptionPlan");

            entity.HasIndex(e => e.Name, "SubscriptionPlan_Name_key").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.DurationDays).HasDefaultValue(30);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(50);
            entity.Property(e => e.Price).HasDefaultValue(0L);
        });

        modelBuilder.Entity<Task>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Task_pkey");

            entity.ToTable("Task");

            entity.HasIndex(e => e.Deadline, "IX_Task_Deadline_Open").HasFilter("((\"Status\")::text = ANY ((ARRAY['ToDo'::character varying, 'Scheduled'::character varying])::text[]))");

            entity.HasIndex(e => e.MilestoneId, "IX_Task_MilestoneId");

            entity.HasIndex(e => new { e.ProjectId, e.Status }, "IX_Task_ProjectId_Status");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ToDo'::character varying");
            entity.Property(e => e.TaskType)
                .HasMaxLength(10)
                .HasDefaultValueSql("'Flexible'::character varying");
            entity.Property(e => e.Title).HasMaxLength(200);

            entity.HasOne(d => d.Domain).WithMany(p => p.Tasks)
                .HasForeignKey(d => d.DomainId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("Task_DomainId_fkey");

            entity.HasOne(d => d.Milestone).WithMany(p => p.Tasks)
                .HasForeignKey(d => d.MilestoneId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("Task_MilestoneId_fkey");

            entity.HasOne(d => d.Project).WithMany(p => p.Tasks)
                .HasForeignKey(d => d.ProjectId)
                .HasConstraintName("Task_ProjectId_fkey");
        });

        modelBuilder.Entity<TaskExecutionMemory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("TaskExecutionMemory_pkey");

            entity.ToTable("TaskExecutionMemory");

            entity.HasIndex(e => e.EmbeddingStatus, "IX_TaskExecutionMemory_EmbeddingPending").HasFilter("((\"EmbeddingStatus\")::text <> 'Done'::text)");

            entity.HasIndex(e => e.TaskId, "IX_TaskExecutionMemory_TaskId");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.EmbeddingStatus)
                .HasMaxLength(10)
                .HasDefaultValueSql("'Pending'::character varying");
            entity.Property(e => e.Outcome).HasMaxLength(20);

            entity.HasOne(d => d.SubTask).WithMany(p => p.TaskExecutionMemories)
                .HasForeignKey(d => d.SubTaskId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("TaskExecutionMemory_SubTaskId_fkey");

            entity.HasOne(d => d.Task).WithMany(p => p.TaskExecutionMemories)
                .HasForeignKey(d => d.TaskId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("TaskExecutionMemory_TaskId_fkey");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("User_pkey");

            entity.ToTable("User");

            entity.HasIndex(e => e.GoogleSubjectId, "User_GoogleSubjectId_key").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.DefaultBufferMinutes).HasDefaultValue(15);
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.GoogleSubjectId).HasMaxLength(64);
            entity.Property(e => e.OnboardingCompleted).HasDefaultValue(false);
            entity.Property(e => e.ReminderLeadMinutes).HasDefaultValue(15);
            entity.Property(e => e.TimeZone)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Asia/Ho_Chi_Minh'::character varying");
            entity.Property(e => e.WorkDayEnd).HasDefaultValueSql("'18:00:00'::time without time zone");
            entity.Property(e => e.WorkDayStart).HasDefaultValueSql("'08:00:00'::time without time zone");
            entity.Property(e => e.WorksOnWeekend).HasDefaultValue(false);

            entity.HasOne(d => d.Role).WithMany(p => p.Users)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("User_RoleId_fkey");
        });

        modelBuilder.Entity<UserCircadianProfile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("UserCircadianProfile_pkey");

            entity.ToTable("UserCircadianProfile");

            entity.HasIndex(e => new { e.UserId, e.HourOfDay }, "UQ_UserCircadianProfile_User_Hour").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Score)
                .HasPrecision(4, 3)
                .HasDefaultValueSql("0.500");
            entity.Property(e => e.Source)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Onboarding'::character varying");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.User).WithMany(p => p.UserCircadianProfiles)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("UserCircadianProfile_UserId_fkey");
        });

        modelBuilder.Entity<UserDomainProfile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("UserDomainProfile_pkey");

            entity.ToTable("UserDomainProfile");

            entity.HasIndex(e => new { e.UserId, e.DomainId }, "UQ_UserDomainProfile_User_Domain").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Kbias)
                .HasPrecision(5, 3)
                .HasDefaultValueSql("1.250")
                .HasColumnName("KBias");
            entity.Property(e => e.SampleCount).HasDefaultValue(0);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Domain).WithMany(p => p.UserDomainProfiles)
                .HasForeignKey(d => d.DomainId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("UserDomainProfile_DomainId_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.UserDomainProfiles)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("UserDomainProfile_UserId_fkey");
        });

        modelBuilder.Entity<UserSubscription>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("UserSubscription_pkey");

            entity.ToTable("UserSubscription");

            entity.HasIndex(e => new { e.UserId, e.Status }, "IX_UserSubscription_UserId_Status");

            entity.HasIndex(e => e.UserId, "UX_UserSubscription_OneActivePerUser")
                .IsUnique()
                .HasFilter("((\"Status\")::text = 'Active'::text)");

            entity.HasIndex(e => e.PaymentTransactionId, "UserSubscription_PaymentTransactionId_key").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.AiCallsUsed).HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.QuotaResetAt).HasDefaultValueSql("now()");
            entity.Property(e => e.StartAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Active'::character varying");

            entity.HasOne(d => d.PaymentTransaction).WithOne(p => p.UserSubscription)
                .HasForeignKey<UserSubscription>(d => d.PaymentTransactionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("UserSubscription_PaymentTransactionId_fkey");

            entity.HasOne(d => d.Plan).WithMany(p => p.UserSubscriptions)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("UserSubscription_PlanId_fkey");

            entity.HasOne(d => d.User).WithOne(p => p.UserSubscription)
                .HasForeignKey<UserSubscription>(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("UserSubscription_UserId_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
