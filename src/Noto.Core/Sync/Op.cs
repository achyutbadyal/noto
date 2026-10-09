using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Noto.Core.Sync;

public static class EntityTypes
{
    public const string TodoItem = "todo_item";
    public const string Workspace = "workspace";
    public const string ItemEvent = "item_event";
    public const string RecurrenceRule = "recurrence_rule";
    public const string Tag = "tag";
    public const string TodoTag = "todo_tag";
    public const string DayNote = "day_note";
    public const string TodoLink = "todo_link";

    public static IReadOnlyList<string> All { get; } =
        SyncEntities.All.Select(e => e.Type).ToList();

    // Entities stored by SyncRowStore (plain SQL rows) rather than a typed mapper.
    public static bool IsRowStoreType(string type) =>
        type is RecurrenceRule or Tag or TodoTag or DayNote or TodoLink;

    // Immutable rows sync as idempotent inserts; everything else is per-field LWW.
    public static bool IsImmutable(string type) => type == ItemEvent;

    // Losing concurrent values of these are kept in the local conflict log (UC-05).
    public static bool IsTextField(string type, string field) =>
        (type == TodoItem && field is "title" or "notes") || (type == DayNote && field == "text");
}

public static class OpKinds
{
    public const string Set = "set";
    public const string Insert = "insert";
}

// One synced change. `Seq` is server-assigned and only present on ops pulled from the server.
public sealed record Op(
    [property: JsonPropertyName("op_id")] Guid OpId,
    [property: JsonPropertyName("entity_type")] string EntityType,
    [property: JsonPropertyName("entity_id")] Guid EntityId,
    [property: JsonPropertyName("workspace_id")] Guid WorkspaceId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("field")] string? Field,
    [property: JsonPropertyName("value")] JsonNode? Value,
    [property: JsonPropertyName("hlc")] string Hlc,
    [property: JsonPropertyName("device_id")] Guid DeviceId
)
{
    [JsonPropertyName("seq")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Seq { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(Op))]
[JsonSerializable(typeof(List<Op>))]
public sealed partial class SyncJson : JsonSerializerContext;

public sealed record SyncConflict(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string Field,
    string? LosingValue,
    string LosingHlc,
    DateTimeOffset RecordedAt
);
