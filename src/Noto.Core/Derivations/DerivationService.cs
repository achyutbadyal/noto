using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Time;

namespace Noto.Core.Derivations;

// Read-side orchestration: loads state + events, derives, and caches. Never writes user data.
public sealed class DerivationService(IUnitOfWork uow, IClock clock)
{
    public Task<TodayView> GetTodayAsync(Guid workspaceId) =>
        uow.RunAsync(async store =>
        {
            var ws =
                await store.Workspaces.GetAsync(workspaceId)
                ?? throw new InvalidOperationException("Workspace not found");
            var today = LogicalDate.Today(ws, clock);
            var items = await store.Items.ListForTodayAsync(workspaceId, today);
            // Only live items show carry/age; done items skip the replay.
            var live = items.Where(i => i.Status is ItemStatus.Open or ItemStatus.Waiting).ToList();
            var metrics = await MetricsAsync(store, ws, live, today);
            return TodayQuery.Build(
                items,
                ws,
                today,
                i => metrics.GetValueOrDefault(i.Id) ?? new ItemMetrics(0, 0, 0)
            );
        });

    public Task<IReadOnlyDictionary<Guid, ItemMetrics>> GetMetricsAsync(
        Guid workspaceId,
        IReadOnlyList<TodoItem> items
    ) =>
        uow.RunAsync(async store =>
        {
            var ws =
                await store.Workspaces.GetAsync(workspaceId)
                ?? throw new InvalidOperationException("Workspace not found");
            return await MetricsAsync(store, ws, items, LogicalDate.Today(ws, clock));
        });

    // Past days are cached; today (and later) is always recomputed because it's still changing.
    public Task<IReadOnlyList<DayStats>> GetDayStatsAsync(
        Guid workspaceId,
        DateOnly from,
        DateOnly to
    ) =>
        uow.RunAsync(async store =>
        {
            var ws =
                await store.Workspaces.GetAsync(workspaceId)
                ?? throw new InvalidOperationException("Workspace not found");
            var today = LogicalDate.Today(ws, clock);
            var cached = await store.Caches.GetDayStatsAsync(workspaceId, from, to);

            var missing = Enumerable
                .Range(0, to.DayNumber - from.DayNumber + 1)
                .Select(n => from.AddDays(n))
                .Where(d => !cached.ContainsKey(d))
                .ToList();

            var computed = new Dictionary<DateOnly, DayStats>(cached);
            if (missing.Count > 0)
            {
                var histories = await HistoriesAsync(store, ws);
                var fallback = DayStatsCalculator.MedianEstimate(histories.Select(h => h.Item));
                foreach (var day in missing)
                {
                    var stats = DayStatsCalculator.Compute(day, histories, fallback);
                    computed[day] = stats;
                    if (day < today)
                        await store.Caches.PutDayStatsAsync(workspaceId, stats);
                }
            }
            return (IReadOnlyList<DayStats>)
                computed.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
        });

    // Time travel: exactly the item sets behind a past day's numbers.
    public Task<DaySets> GetDaySetsAsync(Guid workspaceId, DateOnly day) =>
        uow.RunAsync(async store =>
        {
            var ws =
                await store.Workspaces.GetAsync(workspaceId)
                ?? throw new InvalidOperationException("Workspace not found");
            return DayStatsCalculator.Sets(day, await HistoriesAsync(store, ws));
        });

    static async Task<IReadOnlyList<ItemHistory>> HistoriesAsync(IStore store, Workspace ws)
    {
        var items = await store.Items.ListAsync(ws.Id);
        var events = (await store.Events.ListForWorkspaceAsync(ws.Id)).ToLookup(e => e.ItemId);
        return items
            .Select(i => new ItemHistory(
                i,
                ItemTimeline.Build(i, events[i.Id].ToList(), ws.DayBoundary)
            ))
            .ToList();
    }

    static async Task<IReadOnlyDictionary<Guid, ItemMetrics>> MetricsAsync(
        IStore store,
        Workspace ws,
        IReadOnlyList<TodoItem> items,
        DateOnly today
    )
    {
        var ids = items.Select(i => i.Id).ToList();
        var result = new Dictionary<Guid, ItemMetrics>(
            await store.Caches.GetMetricsAsync(ids, today)
        );

        var missing = items.Where(i => !result.ContainsKey(i.Id)).ToList();
        if (missing.Count == 0)
            return result;

        var events = (
            await store.Events.ListForItemsAsync(missing.Select(i => i.Id).ToList())
        ).ToLookup(e => e.ItemId);
        foreach (var item in missing)
        {
            var metrics = MetricsCalculator.Compute(
                ItemTimeline.Build(item, events[item.Id].ToList(), ws.DayBoundary),
                today
            );
            result[item.Id] = metrics;
            await store.Caches.PutMetricsAsync(item.Id, today, metrics);
        }
        return result;
    }
}
