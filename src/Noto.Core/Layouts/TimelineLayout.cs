using Noto.Core.Models;

namespace Noto.Core.Layouts;

public sealed record TimelineDay(DateOnly Day, IReadOnlyList<TodoItem> Items);

public sealed record TimelineView(
    IReadOnlyList<TodoItem> Overdue, IReadOnlyList<TimelineDay> Days,
    IReadOnlyList<TodoItem> Later, IReadOnlyList<TodoItem> NoDate);

public static class TimelineLayout
{
    // Overdue (past due date) is pinned on top. An item's date is its due date, else a planned day today or later.
    // Everything else lands in the No date lane; nothing is required to have a date.
    public static TimelineView Build(IEnumerable<TodoItem> items, DateOnly today, int daysAhead = 14)
    {
        var live = items.Where(i => i.DeletedAt is null && !i.IsContainer && i.Status is ItemStatus.Open or ItemStatus.Waiting).ToList();
        DateOnly? Key(TodoItem i) => i.DueDate ?? (i.PlannedFor >= today ? i.PlannedFor : null);

        var overdue = live.Where(i => i.DueDate < today).OrderBy(i => i.DueDate).ThenByDescending(i => i.Priority).ToList();
        var rest = live.Except(overdue).ToList();
        var end = today.AddDays(daysAhead);

        var days = Enumerable.Range(0, daysAhead).Select(n => today.AddDays(n))
            .Select(d => new TimelineDay(d, rest.Where(i => Key(i) == d).OrderByDescending(i => i.Priority)
                .ThenBy(i => i.ManualRank, StringComparer.Ordinal).ToList()))
            .ToList();
        var later = rest.Where(i => Key(i) >= end).OrderBy(Key).ToList();
        var noDate = rest.Where(i => Key(i) is null).OrderBy(i => i.ManualRank, StringComparer.Ordinal).ToList();
        return new TimelineView(overdue, days, later, noDate);
    }
}
