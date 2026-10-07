using System.Text.Json.Nodes;
using Noto.Core.Models;

namespace Noto.Core.Commands;

public sealed record CommandContext(DateTimeOffset Now, string Tz, DateOnly Today, Workspace Workspace);

// `Undo*` is the compensating event appended when the command is undone.
// `Spawned` are new child items created in the same transaction (break down).
public sealed record Change(ItemEventType Type, JsonObject? Data, ItemEventType UndoType, JsonObject? UndoData)
{
    public IReadOnlyList<TodoItem> Spawned { get; init; } = [];
}

public sealed record CommandResult(Guid ItemId, Guid UndoToken);

public sealed class CommandException(string message) : Exception(message);

public sealed class InvariantViolationException(IReadOnlyList<string> violations)
    : Exception(string.Join("; ", violations));

public abstract record ItemCommand(Guid ItemId)
{
    public abstract Change Apply(TodoItem item, CommandContext ctx);
}
