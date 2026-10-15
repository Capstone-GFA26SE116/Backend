using System.Text.Json;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using DomainEntity = METANOIA.Domain.Entities.Domain;

namespace METANOIA.Infrastructure.Data
{
    // Ghi AdminActionLog cho mọi thay đổi mà Admin gây ra ở các bảng quản trị, NGAY TRONG lần SaveChanges đó
    // (cùng transaction: thay đổi được lưu thì log cũng được lưu, và ngược lại).
    // Chỉ chạy khi endpoint hiện tại là thao tác ghi chỉ dành cho Admin (IAdminAuditContext.IsActive); thay đổi do hệ thống
    // (webhook thanh toán, job nền) hay do người dùng thường tự thực hiện thì không ghi.
    public class AdminAuditSaveChangesInterceptor : SaveChangesInterceptor
    {
        private const int MaxActionLength = 50;
        private const int MaxReasonLength = 1000;

        private static readonly HashSet<Type> AuditedTypes = new()
        {
            typeof(DomainEntity),
            typeof(SubscriptionPlan),
            typeof(UserSubscription),
            typeof(Invoice),
            typeof(PaymentTransaction),
            typeof(PaymentRefund),
            typeof(FocusTrack),
            typeof(User)
        };

        private readonly IAdminAuditContext _auditContext;

        public AdminAuditSaveChangesInterceptor(IAdminAuditContext auditContext)
        {
            _auditContext = auditContext;
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Audit(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Audit(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void Audit(DbContext? context)
        {
            if (context is null || !_auditContext.IsActive || _auditContext.AdminId is not { } adminId)
            {
                return;
            }

            var reason = _auditContext.Reason.Length > MaxReasonLength ? _auditContext.Reason[..MaxReasonLength] : _auditContext.Reason;
            var now = DateTime.UtcNow;
            var logs = new List<AdminActionLog>();

            foreach (var entry in context.ChangeTracker.Entries().ToList())
            {
                if (!AuditedTypes.Contains(entry.Entity.GetType()))
                {
                    continue;
                }

                var verb = entry.State switch
                {
                    EntityState.Added => "Create",
                    EntityState.Modified => "Update",
                    EntityState.Deleted => "Delete",
                    _ => null
                };
                if (verb is null)
                {
                    continue;
                }

                var targetType = entry.Metadata.ClrType.Name;
                var (oldValue, newValue) = Snapshot(entry);
                if (entry.State == EntityState.Modified && newValue!.Count == 0)
                {
                    continue;
                }

                var action = $"{targetType}.{verb}";
                logs.Add(new AdminActionLog
                {
                    Id = Guid.NewGuid(),
                    AdminId = adminId,
                    Action = action.Length > MaxActionLength ? action[..MaxActionLength] : action,
                    TargetType = targetType,
                    TargetId = (Guid)entry.Property("Id").OriginalValue!,
                    OldValue = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
                    NewValue = newValue is null ? null : JsonSerializer.Serialize(newValue),
                    Reason = reason,
                    CreatedAt = now
                });
            }

            if (logs.Count > 0)
            {
                context.Set<AdminActionLog>().AddRange(logs);
            }
        }

        // Create: chỉ NewValue (toàn bộ). Delete: chỉ OldValue (toàn bộ). Update: chỉ các trường thực sự đổi, trước và sau
        private static (Dictionary<string, object?>? Old, Dictionary<string, object?>? New) Snapshot(EntityEntry entry)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    return (null, entry.Properties.Where(IsLoggable).ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));

                case EntityState.Deleted:
                    return (entry.Properties.Where(IsLoggable).ToDictionary(p => p.Metadata.Name, p => p.OriginalValue), null);

                default:
                    var changed = entry.Properties
                        .Where(p => IsLoggable(p) && p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))
                        .ToList();
                    return (
                        changed.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue),
                        changed.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));
            }
        }

        private static bool IsLoggable(PropertyEntry property) => property.Metadata.ClrType != typeof(byte[]);
    }

    // Dùng khi không có request web (job nền, lệnh bù dữ liệu): không bao giờ ghi AdminActionLog
    public class NullAdminAuditContext : IAdminAuditContext
    {
        public bool IsActive => false;

        public Guid? AdminId => null;

        public string Reason => string.Empty;
    }
}
