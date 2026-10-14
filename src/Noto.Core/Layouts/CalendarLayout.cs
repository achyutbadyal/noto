using Noto.Core.Commands;
using Noto.Core.Models;

namespace Noto.Core.Layouts;

public enum CalendarMode
{
    Month,
    Week,
}

public sealed record CalendarDay(
    DateOnly Day,
    IReadOnlyList<TodoItem> Open,
    IReadOnlyList<TodoItem> Done
);

public sealed record CalendarView(
    DateOnly Start,
    IReadOnlyList<CalendarDay> Days,
    IReadOnlyList<TodoItem> Overdue,
    IReadOnlyList<TodoItem> Unscheduled
);

// Calendar layout: every open item sits on the day it is planned for (else the day it is due). Weeks start on
// Monday. Nothing needs a date: dateless items wait in the Unscheduled tray, and items whose day has passed
// wait in Overdue until you pick a new day for them.
public static class CalendarLayout
{
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    // The first visible day. A month view starts on the Monday on or before the 1st.
    public static DateOnly GridStart(DateOnly anchor, CalendarMode mode) =>
        mode == CalendarMode.Week
            ? WeekStart(anchor)
            : WeekStart(new DateOnly(anchor.Year, anchor.Month, 1));

    // Five weeks when the month fits in them, six otherwise.
    public static int GridWeeks(DateOnly anchor, CalendarMode mode)
    {
        if (mode == CalendarMode.Week)
            return 1;
        var first = new DateOnly(anchor.Year, anchor.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        return (WeekStart(last).DayNumber - WeekStart(first).DayNumber) / 7 + 1;
    }

    // The day an open item is shown on, or null when it has none.
    public static DateOnly? DayOf(TodoItem item) => item.PlannedFor ?? item.DueDate;

    public static CalendarView Build(
        IEnumerable<TodoItem> items,
        DateOnly today,
        DateOnly anchor,
        CalendarMode mode
    )
    {
        var start = GridStart(anchor, mode);
        var count = GridWeeks(anchor, mode) * 7;

        var live = items.Where(i => i.DeletedAt is null && !i.IsContainer).ToList();
        var open = live.Where(i =>
                i.Status is ItemStatus.Open or ItemStatus.Waiting && !i.IsSomeday
            )
            .ToList();

        static int Rank(TodoItem i) => i.Status == ItemStatus.Waiting ? 1 : 0;
        IReadOnlyList<TodoItem> Ordered(IEnumerable<TodoItem> set) =>
            [
                .. set.OrderBy(Rank)
                    .ThenByDescending(i => i.Priority)
                    .ThenBy(i => i.ManualRank, StringComparer.Ordinal),
            ];

        var days = Enumerable
            .Range(0, count)
            .Select(n => start.AddDays(n))
            .Select(d => new CalendarDay(
                d,
                Ordered(open.Where(i => DayOf(i) == d)),
                [
                    .. live.Where(i => i.Status == ItemStatus.Done && i.CompletedOn == d)
                        .OrderBy(i => i.ManualRank, StringComparer.Ordinal),
                ]
            ))
            .ToList();

        return new CalendarView(
            start,
            days,
            Ordered(open.Where(i => DayOf(i) < today)),
            Ordered(open.Where(i => DayOf(i) is null))
        );
    }
}

public static class CalendarMoves
{
    // Translates a drop on a day into ordinary commands. Null `target` is the Unscheduled tray. Done items stay
    // where they were completed, and a day that has passed is not a place to plan new work.
    public static IReadOnlyList<ItemCommand> Plan(TodoItem item, DateOnly? target, DateOnly today)
    {
        if (item.Status is not (ItemStatus.Open or ItemStatus.Waiting) || item.IsContainer)
            return [];
        if (target is null)
            return item.PlannedFor is null
                ? []
                : [new PlanItem(item.Id, null, PlanKind.Unschedule)];
        if (target < today || target == item.PlannedFor)
            return [];
        return [new PlanItem(item.Id, target, PlanKind.Plan)];
    }

    public static bool CanDropOn(DateOnly? target, DateOnly today) =>
        target is null || target >= today;
}
