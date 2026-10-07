using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Sync;
using Noto.Core.Time;

namespace Noto.Core.Commands;

public interface ICommandBus
{
    Task<CommandResult> SendAsync(ItemCommand command);
    Task UndoAsync(Guid undoToken);
    Task SaveWorkspaceAsync(Workspace workspace);
}

// The only writer of items: every command is state change + event in one transaction.
public sealed class CommandBus : ICommandBus
{
    sealed record UndoEntry(
        Guid ItemId,
        TodoItem? Before,
        TodoItem After,
        Change Change,
        IReadOnlyList<Guid> SpawnedIds
    );

    readonly IUnitOfWork uow;
    readonly IClock clock;
    readonly Guid deviceId;
    readonly Dictionary<Guid, UndoEntry> _undo = [];

    public CommandBus(IUnitOfWork uow, IClock clock, Guid deviceId, HybridClock? hlc = null)
    {
        this.uow = uow;
        this.clock = clock;
        this.deviceId = deviceId;
        Hlc = hlc ?? new HybridClock(() => clock.UtcNow.ToUnixTimeMilliseconds(), deviceId);
    }

    // Shared with the sync client so both advance the same clock.
    public HybridClock Hlc { get; }

    public async Task<CommandResult> SendAsync(ItemCommand command)
    {
        var result = await uow.RunAsync(async store =>
        {
            var item = command is CreateItem create
                ? await NewItemAsync(store, create)
                : await store.Items.GetAsync(command.ItemId)
                    ?? throw new CommandException("Item not found");

            var ws =
                await store.Workspaces.GetAsync(item.WorkspaceId)
                ?? throw new CommandException("Workspace not found");
            var ctx = Context(ws);
            var before = command is CreateItem ? null : item.Clone();

            var change = command.Apply(item, ctx);
            await ValidateAsync(store, item);

            await WriteAsync(store, before, item, ws.SyncEnabled);
            var evt = NewEvent(item, change.Type, change.Data, ctx);
            await AppendEventAsync(store, evt, ws.SyncEnabled);
            await InvalidateAsync(store, item, evt, ctx);

            foreach (var child in change.Spawned)
            {
                var violations = ItemInvariants.Check(child, item);
                if (violations.Count > 0)
                    throw new InvariantViolationException(violations);
                await WriteAsync(store, null, child, ws.SyncEnabled);
                await AppendEventAsync(
                    store,
                    NewEvent(
                        child,
                        ItemEventType.Created,
                        new()
                        {
                            ["planned_for"] = child.PlannedFor?.ToString("yyyy-MM-dd"),
                            ["is_someday"] = false,
                            ["source"] = "breakdown",
                        },
                        ctx
                    ),
                    ws.SyncEnabled
                );
                await InvalidateAsync(store, child, evt, ctx);
            }
            return (
                item.Id,
                Entry: new UndoEntry(
                    item.Id,
                    before,
                    item.Clone(),
                    change,
                    change.Spawned.Select(c => c.Id).ToList()
                )
            );
        });

        var token = Guid.CreateVersion7();
        _undo[token] = result.Entry;
        return new CommandResult(result.Id, token);
    }

    public async Task UndoAsync(Guid undoToken)
    {
        if (!_undo.Remove(undoToken, out var entry))
            throw new CommandException("Nothing to undo");

        await uow.RunAsync(async store =>
        {
            var current =
                await store.Items.GetAsync(entry.ItemId)
                ?? throw new CommandException("Item not found");
            var ws = await store.Workspaces.GetAsync(current.WorkspaceId)!;
            var ctx = Context(ws!);

            // Undoing a create tombstones the item; otherwise revert only the fields the command changed,
            // so edits merged from other devices in the meantime survive.
            var restored = entry.Before is null
                ? Tombstone(current, ctx)
                : RevertFields(current, entry.Before, entry.After);
            await WriteAsync(store, current, restored, ws!.SyncEnabled);
            var evt = NewEvent(restored, entry.Change.UndoType, entry.Change.UndoData, ctx);
            await AppendEventAsync(store, evt, ws.SyncEnabled);
            await InvalidateAsync(store, restored, evt, ctx);

            foreach (var childId in entry.SpawnedIds)
            {
                var child = await store.Items.GetAsync(childId);
                if (child is null)
                    continue;
                var gone = Tombstone(child, ctx);
                await WriteAsync(store, child, gone, ws.SyncEnabled);
                await AppendEventAsync(
                    store,
                    NewEvent(gone, ItemEventType.Deleted, null, ctx),
                    ws.SyncEnabled
                );
                await InvalidateAsync(store, gone, evt, ctx);
            }
            return 0;
        });
    }

    // Workspace edits (rename, preset, now item…).
    public Task SaveWorkspaceAsync(Workspace workspace) =>
        uow.RunAsync(async store =>
        {
            await store.Workspaces.UpsertAsync(workspace);
            return 0;
        });

    // Repositories record ops for synced changes themselves, so every writer is covered.
    static Task WriteAsync(IStore store, TodoItem? before, TodoItem after, bool syncEnabled) =>
        store.Items.UpsertAsync(after);

    static Task AppendEventAsync(IStore store, ItemEvent evt, bool syncEnabled) =>
        store.Events.AppendAsync(evt);

    static TodoItem RevertFields(TodoItem current, TodoItem before, TodoItem after)
    {
        var row = SyncRows.ToRow(current);
        var beforeRow = SyncRows.ToRow(before);
        foreach (
            var (field, _) in SyncRows.Diff(beforeRow, SyncRows.ToRow(after), SyncRows.ItemFields)
        )
            row[field] = beforeRow[field]?.DeepClone();
        return SyncRows.ToItem(row);
    }

    CommandContext Context(Workspace ws)
    {
        var tz = LogicalDate.EffectiveZone(ws, clock);
        return new(clock.UtcNow, tz.Id, LogicalDate.Of(clock.UtcNow, tz, ws.DayBoundary), ws);
    }

    async Task<TodoItem> NewItemAsync(IStore store, CreateItem create)
    {
        var ws =
            await store.Workspaces.GetAsync(create.WorkspaceId)
            ?? throw new CommandException("Workspace not found");
        return new TodoItem
        {
            Id = create.ItemId,
            WorkspaceId = ws.Id,
            CreatedAt = clock.UtcNow,
            CreatedTz = LogicalDate.EffectiveZone(ws, clock).Id,
        };
    }

    // Day stats change from the event's logical day on, or from an earlier credited completion day.
    static async Task InvalidateAsync(
        IStore store,
        TodoItem item,
        ItemEvent evt,
        CommandContext ctx
    )
    {
        var from = LogicalDate.Of(evt.OccurredAt, evt.Tz, ctx.Workspace.DayBoundary);
        if (item.CompletedOn is { } credited && credited < from)
            from = credited;
        await store.Caches.InvalidateDayStatsFromAsync(item.WorkspaceId, from);
        await store.Caches.InvalidateMetricsAsync(item.Id);
    }

    static async Task ValidateAsync(IStore store, TodoItem item)
    {
        var parent = item.ParentId is { } pid ? await store.Items.GetAsync(pid) : null;
        var violations = ItemInvariants.Check(item, parent);
        if (violations.Count > 0)
            throw new InvariantViolationException(violations);
    }

    static TodoItem Tombstone(TodoItem item, CommandContext ctx)
    {
        var copy = item.Clone();
        copy.DeletedAt = ctx.Now;
        return copy;
    }

    ItemEvent NewEvent(
        TodoItem item,
        ItemEventType type,
        System.Text.Json.Nodes.JsonObject? data,
        CommandContext ctx
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            ItemId = item.Id,
            WorkspaceId = item.WorkspaceId,
            Type = type,
            Data = data,
            OccurredAt = ctx.Now,
            Tz = ctx.Tz,
            DeviceId = deviceId,
            Hlc = this.Hlc.Next().ToString(),
        };
}
