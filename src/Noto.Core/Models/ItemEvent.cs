using System.Text.Json.Nodes;

namespace Noto.Core.Models;

// Immutable history row; `Hlc` is filled by the sync layer (Phase 8).
public sealed class ItemEvent
{
    public Guid Id { get; init; }
    public Guid ItemId { get; init; }
    public Guid WorkspaceId { get; init; }
    public ItemEventType Type { get; init; }
    public JsonObject? Data { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string Tz { get; init; } = "";
    public Guid DeviceId { get; init; }
    public string Hlc { get; init; } = "";
}
