namespace Noto.Server.Data;

public sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? DigestPrefsJson { get; set; }
}

public sealed class Device
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string Platform { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public long Cursor { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public string TokenHash { get; set; } = "";
    public Guid UserId { get; set; }
    public Guid DeviceId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

// Append-only log; `Seq` is the sync cursor.
public sealed class OpRow
{
    public long Seq { get; set; }
    public Guid OpId { get; set; }
    public Guid UserId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Field { get; set; }
    public string Value { get; set; } = "null";
    public string Hlc { get; set; } = "";
    public Guid DeviceId { get; set; }
    public DateTime ReceivedAt { get; set; }
}

// Materialized LWW state. `RefId` links an event row to its item; `DeletedAt` drives tombstone GC.
public sealed class CurrentRow
{
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public Guid UserId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Row { get; set; } = "{}";
    public string FieldClocks { get; set; } = "{}";
    public long LastSeq { get; set; }
    public string? RefId { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class WorkspaceSync
{
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class ExportJob
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Content { get; set; } = "";
}
