using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class ChatConversation
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? TaskId { get; set; }

    public string Scope { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual ChatMessage? ChatMessage { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual Task? Task { get; set; }
}
