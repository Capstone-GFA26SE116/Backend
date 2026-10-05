using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class ChatMessage
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public string Sender { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string? ProposalJson { get; set; }

    public bool IsApplied { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ChatConversation Conversation { get; set; } = null!;
}
