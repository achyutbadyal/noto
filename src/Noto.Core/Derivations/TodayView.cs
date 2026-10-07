using Noto.Core.Models;
using Noto.Core.Presets;

namespace Noto.Core.Derivations;

public sealed record TodayView(
    TodoItem? Now,
    IReadOnlyList<TodoItem> Pinned,
    IReadOnlyList<TodoItem> Planned,
    IReadOnlyList<TodoItem> Waiting,
    IReadOnlyList<TodoItem> DoneToday)
{
    public int NeedsDecision(DateOnly today) => Planned.Count(i => i.PlannedFor < today) + (Now?.PlannedFor < today ? 1 : 0);
}

public static class TodayQuery
{
    // Day change writes nothing: Today is a pure query over current item state (docs/04 §5).
    public static TodayView Build(
        IEnumerable<TodoItem> items, Workspace ws, DateOnly today, Func<TodoItem, ItemMetrics> metrics)
    {
        var live = items.Where(i => i.DeletedAt is null).ToList();

        var now = live.FirstOrDefault(i => i.Id == ws.NowItemId && i.Status == ItemStatus.Open);
        var planned = live.Where(i =>
            i.Status == ItemStatus.Open && !i.IsContainer && !i.IsSomeday &&
            i.PlannedFor <= today && i.Id != now?.Id);
        var sorted = ItemOrdering.Sort(planned, ws.SortOrderMode, metrics, today);

        // Relentless pressure pins the three highest-carry items on top.
        var pinned = ws.Pressure == Pressure.Relentless
            ? sorted.OrderByDescending(i => metrics(i).Carry).Where(i => metrics(i).Carry > 0).Take(3).ToList()
            : [];
        var rest = sorted.Where(i => !pinned.Contains(i)).ToList();

        var waiting = live.Where(i => i.Status == ItemStatus.Waiting && (i.PlannedFor is null || i.PlannedFor <= today));
        var done = live.Where(i => i.CompletedOn == today).OrderByDescending(i => i.CompletedAt);

        return new TodayView(now, pinned, rest, ItemOrdering.Sort(waiting, ws.SortOrderMode, metrics, today), done.ToList());
    }
}
