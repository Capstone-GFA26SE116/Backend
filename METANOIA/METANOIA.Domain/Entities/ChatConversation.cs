using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class ChatConversation
{
    public Guid Id { get; set; }

    // Chat dự án hoặc chat việc: có ProjectId. Chat nhóm (Scope = Group): ProjectId rỗng, có GroupId (xem docs/sql/008_chat_group.sql)
    public Guid? ProjectId { get; set; }

    public Guid? TaskId { get; set; }

    public Guid? GroupId { get; set; }

    public string Scope { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual ChatMessage? ChatMessage { get; set; }

    public virtual Project? Project { get; set; }

    public virtual WorkGroup? Group { get; set; }

    public virtual Task? Task { get; set; }
}
