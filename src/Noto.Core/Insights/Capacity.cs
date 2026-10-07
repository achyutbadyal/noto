using Noto.Core.Derivations;
using Noto.Core.Models;

namespace Noto.Core.Insights;

public sealed record CapacitySummary(
    int Committed,
    int Capacity,
    CapacityUnit Unit,
    IReadOnlyList<TodoItem> SuggestedDefers
)
{
    public int Over => Math.Max(0, Committed - Capacity);
    public bool IsOver => Committed > Capacity;
}

public static class CapacityBar
{
    // Committed = everything planned for today or earlier, plus the Now item. Done items don't count.
    public static CapacitySummary Compute(
        Workspace ws,
        TodayView view,
        Func<TodoItem, ItemMetrics> metrics,
        int fallbackMinutes
    )
    {
        var open = view
            .Pinned.Concat(view.Planned)
            .Concat(view.Now is null ? [] : [view.Now])
            .ToList();
        int Cost(TodoItem i) =>
            ws.CapacityUnit == CapacityUnit.Items ? 1 : i.EstimateMinutes ?? fallbackMinutes;

        var committed = open.Sum(Cost);
        var suggested = new List<TodoItem>();
        if (committed > ws.DailyCapacity)
        {
            // Defer the lowest-priority, least-carried items first until we fit, at most two.
            var candidates = view
                .Planned.OrderBy(i => i.Priority)
                .ThenBy(i => metrics(i).Carry)
                .ToList();
            var remaining = committed;
            foreach (var item in candidates.Take(2))
            {
                if (remaining <= ws.DailyCapacity)
                    break;
                suggested.Add(item);
                remaining -= Cost(item);
            }
        }
        return new CapacitySummary(committed, ws.DailyCapacity, ws.CapacityUnit, suggested);
    }
}
