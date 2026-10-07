using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Noto.Core.Sync;

namespace Noto.Sync;

public sealed record SyncRequest(
    [property: JsonPropertyName("device_id")] Guid DeviceId,
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("workspaces")] IReadOnlyList<Guid> Workspaces,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("ops")] IReadOnlyList<Op> Ops
);

public sealed record RejectedOp(
    [property: JsonPropertyName("op_id")] Guid OpId,
    [property: JsonPropertyName("code")] string Code
);

public sealed record SyncResponse(
    [property: JsonPropertyName("accepted_op_ids")] IReadOnlyList<Guid> AcceptedOpIds,
    [property: JsonPropertyName("rejected")] IReadOnlyList<RejectedOp> Rejected,
    [property: JsonPropertyName("ops")] IReadOnlyList<Op> Ops,
    [property: JsonPropertyName("next_cursor")] long NextCursor,
    [property: JsonPropertyName("has_more")] bool HasMore,
    [property: JsonPropertyName("server_hlc")] string ServerHlc
);

public sealed record SnapshotRow(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("row")] JsonObject Row,
    [property: JsonPropertyName("field_clocks")] IReadOnlyDictionary<string, string> FieldClocks
);

public sealed record SnapshotPage(
    [property: JsonPropertyName("seq")] long Seq,
    [property: JsonPropertyName("entity_type")] string EntityType,
    [property: JsonPropertyName("rows")] IReadOnlyList<SnapshotRow> Rows,
    [property: JsonPropertyName("next_after")] string? NextAfter
);

public sealed record ServerWorkspaces(
    [property: JsonPropertyName("workspaces")] IReadOnlyList<Guid> Workspaces
);

[JsonSerializable(typeof(SyncRequest))]
[JsonSerializable(typeof(SyncResponse))]
[JsonSerializable(typeof(SnapshotPage))]
[JsonSerializable(typeof(ServerWorkspaces))]
public sealed partial class ProtocolJson : JsonSerializerContext;

public interface ISyncTransport
{
    Task<SyncResponse> SyncAsync(SyncRequest request, CancellationToken ct = default);
    Task<SnapshotPage> SnapshotAsync(
        Guid workspaceId,
        string entityType,
        string? after,
        int limit,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<Guid>> ListWorkspacesAsync(CancellationToken ct = default);
    Task DeleteWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

// 409: the client's cursor is ahead of the server (e.g. restored backup); it must re-bootstrap.
public sealed class CursorAheadException() : Exception("Cursor is ahead of the server");

// Network/server failure; sync is retried later and never blocks the user.
public sealed class SyncTransportException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed record SyncOptions
{
    public int PushBatch { get; init; } = 500;
    public int PullLimit { get; init; } = 500;
    public int SnapshotPage { get; init; } = 500;
    public TimeSpan Debounce { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(60);
}
