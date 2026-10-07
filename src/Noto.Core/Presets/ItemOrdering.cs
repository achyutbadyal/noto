using Noto.Core.Derivations;
using Noto.Core.Models;

namespace Noto.Core.Presets;

// Order strategies (docs/03 › Order). Applied within a section; sections are decided by the view.
public static class ItemOrdering
{
    public static IReadOnlyList<TodoItem> Sort(
        IEnumerable<TodoItem> items,
        SortOrderMode order,
        Func<TodoItem, ItemMetrics> metrics,
        DateOnly today
    )
    {
        var ranked = order switch
        {
            SortOrderMode.PriorityCarry => items
                .OrderByDescending(i => i.Priority)
                .ThenByDescending(i => metrics(i).Carry),
            SortOrderMode.CarryDesc => items
                .OrderByDescending(i => metrics(i).Carry)
                .ThenByDescending(i => metrics(i).Age),
            SortOrderMode.DueDate => items
                .OrderBy(i => DueGroup(i, today))
                .ThenBy(i => i.DueDate ?? DateOnly.MaxValue)
                .ThenByDescending(i => i.Priority),
            SortOrderMode.TimeOfDay => items.OrderBy(i =>
                i.TimeOfDay is { } t ? (int)t : int.MaxValue
            ),
            _ => items.OrderBy(_ => 0),
        };

        return ranked.ThenBy(i => i.ManualRank, StringComparer.Ordinal).ToList();
    }

    // overdue first, then dated, then undated
    static int DueGroup(TodoItem i, DateOnly today) =>
        i.DueDate is null ? 2
        : i.DueDate < today ? 0
        : 1;
}
