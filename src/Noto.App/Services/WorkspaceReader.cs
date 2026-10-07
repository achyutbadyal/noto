using Noto.Core.Derivations;
using Noto.Core.Insights;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Presets;
using Noto.Core.Time;

namespace Noto.App.Services;

public sealed record WorkspaceSnapshot(
    Workspace Workspace,
    DateOnly Today,
    IReadOnlyList<TodoItem> Items,
    IReadOnlyDictionary<Guid, ItemMetrics> Metrics,
    PressureThresholds Thresholds,
    int FallbackMinutes
)
{
    public TodoItem? Find(Guid id) => Items.FirstOrDefault(i => i.Id == id);

    public ItemMetrics MetricsOf(TodoItem item) =>
        Metrics.GetValueOrDefault(item.Id) ?? new ItemMetrics(0, 0, 0);

    public TodayView Today_ => TodayQuery.Build(Items, Workspace, Today, MetricsOf);

    // Open, planned for an earlier day: each one still owes the user a decision.
    public IReadOnlyList<TodoItem> NeedsDecision =>
        Items
            .Where(i =>
                i.Status == ItemStatus.Open
                && !i.IsContainer
                && !i.IsSomeday
                && i.PlannedFor < Today
            )
            .OrderByDescending(i => MetricsOf(i).Carry)
            .ThenByDescending(i => i.Priority)
            .ToList();
}

// Read side for screens: one consistent snapshot of a workspace per load.
public sealed class WorkspaceReader(IUnitOfWork uow, DerivationService derive, IClock clock)
{
    public async Task<WorkspaceSnapshot> LoadAsync(Guid workspaceId)
    {
        var (ws, items) = await uow.RunAsync(async s =>
            (
                await s.Workspaces.GetAsync(workspaceId)
                    ?? throw new InvalidOperationException("Workspace not found"),
                await s.Items.ListAsync(workspaceId)
            )
        );

        var today = LogicalDate.Today(ws, clock);
        var live = items.Where(i => i.Status is ItemStatus.Open or ItemStatus.Waiting).ToList();
        var metrics = await derive.GetMetricsAsync(workspaceId, live);
        return new WorkspaceSnapshot(
            ws,
            today,
            items,
            metrics,
            PressureThresholds.For(ws.Pressure, ws.PressureOverridesJson),
            DayStatsCalculator.MedianEstimate(items)
        );
    }

    public async Task<int> NeedsDecisionCountAsync(Guid workspaceId) =>
        (await LoadAsync(workspaceId)).NeedsDecision.Count;

    public async Task<IReadOnlyList<DayStats>> DayStatsAsync(
        Guid workspaceId,
        DateOnly from,
        DateOnly to
    ) => await derive.GetDayStatsAsync(workspaceId, from, to);

    public Task<DaySets> DaySetsAsync(Guid workspaceId, DateOnly day) =>
        derive.GetDaySetsAsync(workspaceId, day);

    public Task<IReadOnlyList<ItemEvent>> EventsAsync(Guid itemId) =>
        uow.RunAsync(s => s.Events.ListForItemAsync(itemId));

    public Task<IReadOnlyList<ItemRecord>> RecordsAsync(Guid workspaceId) =>
        uow.RunAsync(async s =>
        {
            var ws =
                await s.Workspaces.GetAsync(workspaceId)
                ?? throw new InvalidOperationException("Workspace not found");
            var items = await s.Items.ListAsync(workspaceId);
            var events = (await s.Events.ListForWorkspaceAsync(workspaceId)).ToLookup(e =>
                e.ItemId
            );
            return (IReadOnlyList<ItemRecord>)
                items
                    .Select(i => new ItemRecord(
                        i,
                        ItemTimeline.Build(i, events[i.Id].ToList(), ws.DayBoundary),
                        events[i.Id].ToList()
                    ))
                    .ToList();
        });
}
