using System.Text.Json.Serialization;
using Noto.Core.Models;

namespace Noto.Core.Derivations;

// Item ids behind each metric, so time travel can render exactly the same sets.
public sealed record DaySets(
    DateOnly Day,
    IReadOnlyList<Guid> CarriedIn,
    IReadOnlyList<Guid> PlannedIn,
    IReadOnlyList<Guid> Added,
    IReadOnlyList<Guid> Done,
    IReadOnlyList<Guid> Dropped,
    IReadOnlyList<Guid> DeferredOut,
    IReadOnlyList<Guid> OpenAtEnd
);

public sealed record DayStats(
    DateOnly Day,
    int CarriedIn,
    int PlannedIn,
    int Added,
    int Done,
    int Dropped,
    int DeferredOut,
    int OpenAtEnd,
    double? CompletionRate,
    int PlannedMinutes
);

[JsonSerializable(typeof(DayStats))]
public sealed partial class DerivationJson : JsonSerializerContext;

public sealed record ItemHistory(TodoItem Item, ItemTimeline Timeline);

public static class DayStatsCalculator
{
    public static DaySets Sets(DateOnly day, IReadOnlyList<ItemHistory> histories)
    {
        List<Guid> carried = [],
            planned = [],
            added = [],
            done = [],
            dropped = [],
            deferred = [],
            open = [];
        var next = day.AddDays(1);

        foreach (var (item, t) in histories)
        {
            if (item.DeletedAt is not null)
                continue;
            var start = t.StateAtStartOf(day);
            var end = t.StateAtStartOf(next);
            var final = t.Final;

            if (Live(start) && start.PlannedFor < day)
                carried.Add(item.Id);
            if (Live(start) && start.PlannedFor == day)
                planned.Add(item.Id);
            if (t.CreatedOn == day && t.CreatedPlannedFor == day)
                added.Add(item.Id);
            if (final.Status == ItemStatus.Done && final.CompletedOn == day)
                done.Add(item.Id);
            if (final.Status == ItemStatus.Dropped && final.DroppedOn == day)
                dropped.Add(item.Id);
            if (t.PlanEvents.Any(p => p.Date == day && p.Kind == "defer"))
                deferred.Add(item.Id);

            // "Already done" credited to a day at or before D removes the item from that day's leftovers.
            var creditedByD = final.Status == ItemStatus.Done && final.CompletedOn <= day;
            if (
                end.Status == ItemStatus.Open
                && !end.IsSomeday
                && !end.IsContainer
                && end.PlannedFor <= day
                && !creditedByD
            )
                open.Add(item.Id);
        }
        return new DaySets(day, carried, planned, added, done, dropped, deferred, open);
    }

    public static DayStats Compute(
        DateOnly day,
        IReadOnlyList<ItemHistory> histories,
        int fallbackEstimateMinutes
    )
    {
        var s = Sets(day, histories);
        var committed = s.CarriedIn.Concat(s.PlannedIn).Concat(s.Added).ToHashSet();
        var minutes = histories
            .Where(h => committed.Contains(h.Item.Id))
            .Sum(h => h.Item.EstimateMinutes ?? fallbackEstimateMinutes);
        var denominator = s.Done.Count + s.OpenAtEnd.Count;

        return new DayStats(
            day,
            s.CarriedIn.Count,
            s.PlannedIn.Count,
            s.Added.Count,
            s.Done.Count,
            s.Dropped.Count,
            s.DeferredOut.Count,
            s.OpenAtEnd.Count,
            denominator == 0 ? null : (double)s.Done.Count / denominator,
            minutes
        );
    }

    // Median estimate across items that have one; used when an item has none.
    public static int MedianEstimate(IEnumerable<TodoItem> items, int fallback = 30)
    {
        var sorted = items
            .Where(i => i.EstimateMinutes is > 0)
            .Select(i => i.EstimateMinutes!.Value)
            .Order()
            .ToList();
        return sorted.Count == 0 ? fallback : sorted[sorted.Count / 2];
    }

    static bool Live(ItemState s) => s.IsPlannedOpen;
}
