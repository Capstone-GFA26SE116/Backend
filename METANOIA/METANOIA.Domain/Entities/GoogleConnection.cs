using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class GoogleConnection
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public byte[] AccessTokenEncrypted { get; set; } = null!;

    public byte[] RefreshTokenEncrypted { get; set; } = null!;

    public DateTime TokenExpiresAt { get; set; }

    public string Status { get; set; } = null!;

    public string? SyncToken { get; set; }

    public string? WatchChannelId { get; set; }

    public DateTime? WatchExpiresAt { get; set; }

    public DateTime? LastSyncedAt { get; set; }

    public string? LastSyncError { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<GoogleCalendarEvent> GoogleCalendarEvents { get; set; } = new List<GoogleCalendarEvent>();

    public virtual User User { get; set; } = null!;
}
