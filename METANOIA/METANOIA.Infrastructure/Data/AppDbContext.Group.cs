using METANOIA.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace METANOIA.Infrastructure.Data;

// Ánh xạ các bảng nhánh Group (docs/sql/005_group_tables.sql). Đặt trong file riêng để không sửa file scaffold AppDbContext.cs.
public partial class AppDbContext
{
    public virtual DbSet<WorkGroup> WorkGroups { get; set; }

    public virtual DbSet<GroupMember> GroupMembers { get; set; }

    public virtual DbSet<GroupInvitation> GroupInvitations { get; set; }

    public virtual DbSet<GroupMilestone> GroupMilestones { get; set; }

    public virtual DbSet<GroupTask> GroupTasks { get; set; }

    public virtual DbSet<GroupSubTask> GroupSubTasks { get; set; }

    public virtual DbSet<GroupTaskComment> GroupTaskComments { get; set; }

    public virtual DbSet<GroupActivityLog> GroupActivityLogs { get; set; }

    public virtual DbSet<GroupFocusSession> GroupFocusSessions { get; set; }

    public virtual DbSet<GroupTaskChangeRequest> GroupTaskChangeRequests { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkGroup>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("WorkGroup_pkey");
            entity.ToTable("WorkGroup");
            entity.HasIndex(e => e.OwnerId, "IX_WorkGroup_OwnerId");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValueSql("'Active'::character varying");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Owner).WithMany()
                .HasForeignKey(d => d.OwnerId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("WorkGroup_OwnerId_fkey");
        });

        modelBuilder.Entity<GroupMember>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("GroupMember_pkey");
            entity.ToTable("GroupMember");
            entity.HasIndex(e => new { e.GroupId, e.UserId }, "GroupMember_GroupId_UserId_key").IsUnique();
            entity.HasIndex(e => e.UserId, "IX_GroupMember_UserId");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Role).HasMaxLength(10);
            entity.Property(e => e.JoinedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Group).WithMany(p => p.Members)
                .HasForeignKey(d => d.GroupId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("GroupMember_GroupId_fkey");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("GroupMember_UserId_fkey");
        });

        modelBuilder.Entity<GroupInvitation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("GroupInvitation_pkey");
            entity.ToTable("GroupInvitation");
            entity.HasIndex(e => e.TokenHash, "GroupInvitation_TokenHash_key").IsUnique();
            entity.HasIndex(e => e.Email, "IX_GroupInvitation_Email");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Email).HasMaxLength(254);
            entity.Property(e => e.Role).HasMaxLength(10);
            entity.Property(e => e.TokenHash).HasMaxLength(64);
            entity.Property(e => e.Status).HasMaxLength(10).HasDefaultValueSql("'Pending'::character varying");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Group).WithMany(p => p.Invitations)
                .HasForeignKey(d => d.GroupId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("GroupInvitation_GroupId_fkey");

            entity.HasOne(d => d.InvitedBy).WithMany()
                .HasForeignKey(d => d.InvitedById)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("GroupInvitation_InvitedById_fkey");
        });

        modelBuilder.Entity<GroupMilestone>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("GroupMilestone_pkey");
            entity.ToTable("GroupMilestone");
            entity.HasIndex(e => new { e.GroupId, e.OrderIndex }, "GroupMilestone_GroupId_OrderIndex_key").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Status).HasMaxLength(15).HasDefaultValueSql("'Planned'::character varying");
            entity.Property(e => e.RevisionCount).HasDefaultValue(0);

            entity.HasOne(d => d.Group).WithMany(p => p.Milestones)
                .HasForeignKey(d => d.GroupId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("GroupMilestone_GroupId_fkey");
        });

        modelBuilder.Entity<GroupTask>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("GroupTask_pkey");
            entity.ToTable("GroupTask");
            entity.HasIndex(e => new { e.GroupId, e.Status }, "IX_GroupTask_GroupId_Status");
            entity.HasIndex(e => e.AssigneeId, "IX_GroupTask_AssigneeId");
            entity.HasIndex(e => e.MilestoneId, "IX_GroupTask_MilestoneId");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Title).HasMaxLength(300);
            entity.Property(e => e.Priority).HasMaxLength(10).HasDefaultValueSql("'Medium'::character varying");
            entity.Property(e => e.Status).HasMaxLength(15).HasDefaultValueSql("'ToDo'::character varying");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Group).WithMany(p => p.Tasks)
                .HasForeignKey(d => d.GroupId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("GroupTask_GroupId_fkey");

            entity.HasOne(d => d.Milestone).WithMany(p => p.Tasks)
                .HasForeignKey(d => d.MilestoneId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("GroupTask_MilestoneId_fkey");

            entity.HasOne(d => d.Assignee).WithMany()
                .HasForeignKey(d => d.AssigneeId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("GroupTask_AssigneeId_fkey");
        });

        modelBuilder.Entity<GroupSubTask>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("GroupSubTask_pkey");
            entity.ToTable("GroupSubTask");
            entity.HasIndex(e => new { e.GroupTaskId, e.OrderIndex }, "GroupSubTask_GroupTaskId_OrderIndex_key").IsUnique();
            entity.HasIndex(e => e.AssigneeId, "IX_GroupSubTask_AssigneeId");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Title).HasMaxLength(300);
            entity.Property(e => e.Status).HasMaxLength(15).HasDefaultValueSql("'ToDo'::character varying");

            entity.HasOne(d => d.GroupTask).WithMany(p => p.SubTasks)
                .HasForeignKey(d => d.GroupTaskId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("GroupSubTask_GroupTaskId_fkey");

            entity.HasOne(d => d.Assignee).WithMany()
                .HasForeignKey(d => d.AssigneeId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("GroupSubTask_AssigneeId_fkey");

            entity.HasOne(d => d.CompletedBy).WithMany()
                .HasForeignKey(d => d.CompletedById)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("GroupSubTask_CompletedById_fkey");
        });

        modelBuilder.Entity<GroupTaskComment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("GroupTaskComment_pkey");
            entity.ToTable("GroupTaskComment");
            entity.HasIndex(e => new { e.GroupTaskId, e.CreatedAt }, "IX_GroupTaskComment_GroupTaskId");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.GroupTask).WithMany(p => p.Comments)
                .HasForeignKey(d => d.GroupTaskId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("GroupTaskComment_GroupTaskId_fkey");

            entity.HasOne(d => d.Author).WithMany()
                .HasForeignKey(d => d.AuthorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("GroupTaskComment_AuthorId_fkey");
        });

        modelBuilder.Entity<GroupActivityLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("GroupActivityLog_pkey");
            entity.ToTable("GroupActivityLog");
            entity.HasIndex(e => new { e.GroupId, e.CreatedAt }, "IX_GroupActivityLog_GroupId_CreatedAt");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.EntityType).HasMaxLength(20);
            entity.Property(e => e.Action).HasMaxLength(40);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Group).WithMany(p => p.ActivityLogs)
                .HasForeignKey(d => d.GroupId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("GroupActivityLog_GroupId_fkey");

            entity.HasOne(d => d.Actor).WithMany()
                .HasForeignKey(d => d.ActorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("GroupActivityLog_ActorId_fkey");
        });

        modelBuilder.Entity<GroupFocusSession>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("GroupFocusSession_pkey");
            entity.ToTable("GroupFocusSession");
            entity.HasIndex(e => e.GroupTaskId, "IX_GroupFocusSession_GroupTaskId");
            entity.HasIndex(e => new { e.UserId, e.StartedAt }, "IX_GroupFocusSession_UserId");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.StopReason).HasMaxLength(15);
            entity.Property(e => e.CaptureMethod).HasMaxLength(15).HasDefaultValueSql("'Timer'::character varying");

            entity.HasOne(d => d.GroupTask).WithMany(p => p.FocusSessions)
                .HasForeignKey(d => d.GroupTaskId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("GroupFocusSession_GroupTaskId_fkey");

            entity.HasOne(d => d.GroupSubTask).WithMany()
                .HasForeignKey(d => d.GroupSubTaskId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("GroupFocusSession_GroupSubTaskId_fkey");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("GroupFocusSession_UserId_fkey");
        });

        modelBuilder.Entity<GroupTaskChangeRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("GroupTaskChangeRequest_pkey");
            entity.ToTable("GroupTaskChangeRequest");
            entity.HasIndex(e => new { e.GroupTaskId, e.CreatedAt }, "IX_GroupTaskChangeRequest_GroupTaskId");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Reason).HasMaxLength(1000);
            entity.Property(e => e.Status).HasMaxLength(10).HasDefaultValueSql("'Pending'::character varying");
            entity.Property(e => e.DecisionNote).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.GroupTask).WithMany(p => p.ChangeRequests)
                .HasForeignKey(d => d.GroupTaskId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("GroupTaskChangeRequest_GroupTaskId_fkey");

            entity.HasOne(d => d.RequestedBy).WithMany()
                .HasForeignKey(d => d.RequestedById)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("GroupTaskChangeRequest_RequestedById_fkey");

            entity.HasOne(d => d.DecidedBy).WithMany()
                .HasForeignKey(d => d.DecidedById)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("GroupTaskChangeRequest_DecidedById_fkey");
        });
    }
}
