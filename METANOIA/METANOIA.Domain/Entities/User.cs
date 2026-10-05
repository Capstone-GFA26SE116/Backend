using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class User
{
    public Guid Id { get; set; }

    public Guid RoleId { get; set; }

    public string Email { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string GoogleSubjectId { get; set; } = null!;

    public string TimeZone { get; set; } = null!;

    public TimeOnly WorkDayStart { get; set; }

    public TimeOnly WorkDayEnd { get; set; }

    public bool WorksOnWeekend { get; set; }

    public int DefaultBufferMinutes { get; set; }

    public int ReminderLeadMinutes { get; set; }

    public bool OnboardingCompleted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<AdminActionLog> AdminActionLogs { get; set; } = new List<AdminActionLog>();

    public virtual ICollection<Client> Clients { get; set; } = new List<Client>();

    public virtual GoogleConnection? GoogleConnection { get; set; }

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<PaymentTransaction> PaymentTransactions { get; set; } = new List<PaymentTransaction>();

    public virtual Project? Project { get; set; }

    public virtual Role Role { get; set; } = null!;

    public virtual ICollection<ScheduleProposal> ScheduleProposals { get; set; } = new List<ScheduleProposal>();

    public virtual ICollection<UserCircadianProfile> UserCircadianProfiles { get; set; } = new List<UserCircadianProfile>();

    public virtual ICollection<UserDomainProfile> UserDomainProfiles { get; set; } = new List<UserDomainProfile>();

    public virtual UserSubscription? UserSubscription { get; set; }
}
