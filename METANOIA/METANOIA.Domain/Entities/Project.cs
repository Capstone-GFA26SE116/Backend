using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class Project
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? ClientId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? Deadline { get; set; }

    public string Status { get; set; } = null!;

    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<ChatConversation> ChatConversations { get; set; } = new List<ChatConversation>();

    public virtual Client? Client { get; set; }

    public virtual ICollection<Milestone> Milestones { get; set; } = new List<Milestone>();

    public virtual ICollection<ProjectDocument> ProjectDocuments { get; set; } = new List<ProjectDocument>();

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();

    public virtual User User { get; set; } = null!;
}
