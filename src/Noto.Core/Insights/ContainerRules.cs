using Noto.Core.Commands;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Time;

namespace Noto.Core.Insights;

public sealed record ContainerProgress(int Done, int Total)
{
    public override string ToString() => $"{Done}/{Total}";
}

public enum ContainerOutcome
{
    StillOpen,
    AutoComplete,
    AskDoneOrDrop,
}

// Subtask rules from docs/04 §2.2. Pure; `ContainerService` applies them.
public static class ContainerRules
{
    static IEnumerable<TodoItem> Live(IEnumerable<TodoItem> children) =>
        children.Where(c => c.DeletedAt is null);

    // Dropped subtasks no longer count toward the total.
    public static ContainerProgress Progress(IEnumerable<TodoItem> children)
    {
        var live = Live(children).Where(c => c.Status != ItemStatus.Dropped).ToList();
        return new(live.Count(c => c.Status == ItemStatus.Done), live.Count);
    }

    public static ContainerOutcome Evaluate(IEnumerable<TodoItem> children)
    {
        var live = Live(children).ToList();
        if (live.Count == 0 || live.Any(c => c.Status is ItemStatus.Open or ItemStatus.Waiting))
            return ContainerOutcome.StillOpen;
        return live.Any(c => c.Status == ItemStatus.Done)
            ? ContainerOutcome.AutoComplete
            : ContainerOutcome.AskDoneOrDrop;
    }

    // The parent is credited to the day its last subtask was completed.
    public static DateOnly CompletionDay(IEnumerable<TodoItem> children) =>
        Live(children).Where(c => c.CompletedOn is not null).Max(c => c.CompletedOn!.Value);
}

public sealed record ContainerFollowUp(ContainerOutcome Outcome, CommandResult? AutoCompleted);

public sealed class ContainerService(IUnitOfWork uow, ICommandBus bus)
{
    // Call after a subtask is completed or dropped. Completes the parent when nothing is left open.
    public async Task<ContainerFollowUp> AfterChildResolvedAsync(Guid childId)
    {
        var (parent, children) = await uow.RunAsync(async s =>
        {
            var child = await s.Items.GetAsync(childId);
            if (child?.ParentId is not { } pid)
                return ((TodoItem?)null, (IReadOnlyList<TodoItem>)[]);
            var all = await s.Items.ListAsync(child.WorkspaceId);
            return (await s.Items.GetAsync(pid), all.Where(i => i.ParentId == pid).ToList());
        });

        if (parent is null || parent.Status != ItemStatus.Open)
            return new(ContainerOutcome.StillOpen, null);

        var outcome = ContainerRules.Evaluate(children);
        if (outcome != ContainerOutcome.AutoComplete)
            return new(outcome, null);

        var result = await bus.SendAsync(
            new CompleteItem(parent.Id, ContainerRules.CompletionDay(children))
        );
        return new(outcome, result);
    }
}
