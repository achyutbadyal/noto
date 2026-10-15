using Noto.Core.Models;

namespace Noto.Core.Layouts;

// The tasks planned for one part of the day. A null part is "no particular time".
public sealed record DayPartGroup(TimeOfDay? Part, IReadOnlyList<TodoItem> Items, int Cost);

public enum SuggestionReason
{
    DueSoon,
    Overdue,
    HighPriority,
    Unscheduled,
}

public sealed record DaySuggestion(TodoItem Item, SuggestionReason Reason);

// Everything the day-ahead planner shows about one future day, derived from item state alone.
public sealed record DayAheadPlan(
    DateOnly Day,
    IReadOnlyList<DayPartGroup> Parts,
    IReadOnlyList<TodoItem> DueOnly,
    IReadOnlyList<TodoItem> Waiting,
    IReadOnlyList<DaySuggestion> Suggestions,
    int PlannedCount,
    int Committed,
    int Capacity,
    CapacityUnit Unit,
    int Unestimated
)
{
    public bool IsEmpty => PlannedCount == 0 && DueOnly.Count == 0 && Waiting.Count == 0;
    public bool IsOver => Committed > Capacity;
    public int Free => Math.Max(0, Capacity - Committed);
}

// Future days: what is planned, what is merely due, and what could still fit. Nothing here writes; the
// planner's actions are the same plan commands the calendar uses.
public static class DayAheadLayout
{
    const int SuggestionLimit = 5;
    const int DueHorizonDays = 7;

    // What an item costs against the day's capacity: one, or its estimate (the workspace's typical
    // estimate when it has none).
    public static int Cost(TodoItem item, Workspace ws, int fallbackMinutes) =>
        ws.CapacityUnit == CapacityUnit.Items ? 1 : item.EstimateMinutes ?? fallbackMinutes;

    // Items that count toward a day's load: open and planned for it (waiting items are not work you can do).
    public static IReadOnlyList<TodoItem> PlannedOn(IEnumerable<TodoItem> items, DateOnly day) =>
        [
            .. items.Where(i =>
                i.DeletedAt is null
                && !i.IsContainer
                && !i.IsSomeday
                && i.Status == ItemStatus.Open
                && i.PlannedFor == day
            ),
        ];

    public static (int Committed, int Capacity) Load(
        IEnumerable<TodoItem> items,
        DateOnly day,
        Workspace ws,
        int fallbackMinutes
    ) => (PlannedOn(items, day).Sum(i => Cost(i, ws, fallbackMinutes)), ws.DailyCapacity);

    public static DayAheadPlan Build(
        IEnumerable<TodoItem> items,
        DateOnly today,
        DateOnly day,
        Workspace ws,
        int fallbackMinutes,
        Func<TodoItem, int>? carryOf = null
    )
    {
        var live = items.Where(i => i.DeletedAt is null && !i.IsContainer && !i.IsSomeday).ToList();
        IReadOnlyList<TodoItem> Ordered(IEnumerable<TodoItem> set) =>
            [
                .. set.OrderByDescending(i => i.Priority)
                    .ThenBy(i => i.ManualRank, StringComparer.Ordinal),
            ];

        var planned = PlannedOn(live, day);
        var parts = planned
            .GroupBy(i => i.TimeOfDay)
            .OrderBy(g => g.Key is null ? int.MaxValue : (int)g.Key.Value)
            .Select(g =>
            {
                var set = Ordered(g);
                return new DayPartGroup(g.Key, set, set.Sum(i => Cost(i, ws, fallbackMinutes)));
            })
            .ToList();

        var waiting = Ordered(
            live.Where(i => i.Status == ItemStatus.Waiting && i.PlannedFor == day)
        );
        var dueOnly = Ordered(
            live.Where(i =>
                i.Status is ItemStatus.Open or ItemStatus.Waiting
                && i.DueDate == day
                && i.PlannedFor != day
            )
        );

        var committed = planned.Sum(i => Cost(i, ws, fallbackMinutes));
        var unestimated =
            ws.CapacityUnit == CapacityUnit.Minutes
                ? planned.Count(i => i.EstimateMinutes is null)
                : 0;

        return new DayAheadPlan(
            day,
            parts,
            dueOnly,
            waiting,
            Suggest(live, today, day, carryOf ?? (_ => 0)),
            planned.Count,
            committed,
            ws.DailyCapacity,
            ws.CapacityUnit,
            unestimated
        );
    }

    // What could still go on this day: things with a deadline near it, things whose day passed and are still
    // undecided, then the highest-priority items that have no day. A suggestion is only ever a proposal.
    static IReadOnlyList<DaySuggestion> Suggest(
        IReadOnlyList<TodoItem> live,
        DateOnly today,
        DateOnly day,
        Func<TodoItem, int> carryOf
    )
    {
        var open = live.Where(i =>
                i.Status == ItemStatus.Open
                && i.PlannedFor != day
                && (i.PlannedFor is null || i.PlannedFor < today)
            )
            .ToList();

        var dueSoon = open.Where(i => i.DueDate is { } d && d <= day.AddDays(DueHorizonDays))
            .OrderBy(i => i.DueDate)
            .ThenByDescending(i => i.Priority)
            .Select(i => new DaySuggestion(
                i,
                i.DueDate < today ? SuggestionReason.Overdue : SuggestionReason.DueSoon
            ));

        var undecided = open.Where(i => i.PlannedFor < today && i.DueDate is null)
            .OrderByDescending(i => carryOf(i))
            .ThenByDescending(i => i.Priority)
            .Select(i => new DaySuggestion(i, SuggestionReason.Overdue));

        var unscheduled = open.Where(i => i.PlannedFor is null && i.DueDate is null)
            .OrderByDescending(i => i.Priority)
            .ThenBy(i => i.ManualRank, StringComparer.Ordinal)
            .Select(i => new DaySuggestion(
                i,
                i.Priority >= 3 ? SuggestionReason.HighPriority : SuggestionReason.Unscheduled
            ));

        return
        [
            .. dueSoon
                .Concat(undecided)
                .Concat(unscheduled)
                .DistinctBy(s => s.Item.Id)
                .Take(SuggestionLimit),
        ];
    }
}
