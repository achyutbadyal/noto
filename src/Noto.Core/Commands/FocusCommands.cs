using Noto.Core.Models;

namespace Noto.Core.Commands;

// Event-only: focus time feeds estimate-accuracy insights; item state is untouched.
public sealed record StartFocus(Guid ItemId) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx) =>
        new(ItemEventType.FocusStarted, null, ItemEventType.FocusStopped, new() { ["minutes"] = 0 });
}

public sealed record StopFocus(Guid ItemId, int Minutes) : ItemCommand(ItemId)
{
    public override Change Apply(TodoItem item, CommandContext ctx) =>
        new(ItemEventType.FocusStopped, new() { ["minutes"] = Minutes }, ItemEventType.FocusStarted, null);
}
