using Noto.Core.Commands;
using Noto.Core.Insights;

namespace Noto.App.Services;

// Every user action goes through here: runs commands, records one undo entry per action, notifies screens.
public sealed class ActionRunner(ICommandBus bus, UndoService undo, ContainerService containers)
{
    public event Action? Changed;

    public Task RunAsync(ItemCommand command, string label) => RunAllAsync([command], label);

    public async Task RunAllAsync(IReadOnlyList<ItemCommand> commands, string label)
    {
        var tokens = new List<Guid>();
        try
        {
            foreach (var command in commands)
            {
                tokens.Add((await bus.SendAsync(command)).UndoToken);
                if (
                    command is CompleteItem or DropItem
                    && await containers.AfterChildResolvedAsync(command.ItemId)
                        is { AutoCompleted: { } auto }
                )
                    tokens.Add(auto.UndoToken);
            }
        }
        finally
        {
            // Keep whatever succeeded undoable even if a later command in the batch failed.
            if (tokens.Count > 0)
            {
                undo.Push(label, tokens);
                Changed?.Invoke();
            }
        }
    }

    public void NotifyChanged() => Changed?.Invoke();
}
