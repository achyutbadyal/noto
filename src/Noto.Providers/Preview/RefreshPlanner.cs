using Noto.Core.Links;
using Noto.Core.Models;

namespace Noto.Providers.Preview;

// Decides what to refresh and how urgently (docs/10 › When state is checked).
public static class RefreshPlanner
{
    public static readonly TimeSpan ForegroundCycle = TimeSpan.FromMinutes(15);

    // Visible rows first, then Waiting / Planned-today items, then everything else with links.
    public static IReadOnlyList<PreviewRequest> Plan(
        IReadOnlyList<TodoItem> items, IReadOnlyList<TodoLink> links, DateOnly today, IReadOnlySet<Guid> visible)
    {
        var byItem = items.ToDictionary(i => i.Id);
        return links
            .Where(l => byItem.ContainsKey(l.ItemId) && byItem[l.ItemId].Status is ItemStatus.Open or ItemStatus.Waiting)
            .Select(l => (l.Url, Priority: PriorityOf(byItem[l.ItemId], today, visible.Contains(l.ItemId))))
            .GroupBy(x => x.Url)
            .Select(g => new PreviewRequest(g.Key, g.Min(x => x.Priority)))
            .OrderBy(r => r.Priority)
            .ToList();
    }

    // The 15-minute loop only covers items that can react to a link change.
    public static IReadOnlyList<PreviewRequest> ForegroundCycleRequests(
        IReadOnlyList<TodoItem> items, IReadOnlyList<TodoLink> links, DateOnly today) =>
        Plan(items, links, today, new HashSet<Guid>())
            .Where(r => r.Priority != PreviewPriority.Rest)
            .Select(r => r with { Force = false }) // TTLs already gate the network
            .ToList();

    public static bool CycleDue(DateTimeOffset? lastRun, DateTimeOffset now) => lastRun is null || now - lastRun >= ForegroundCycle;

    static PreviewPriority PriorityOf(TodoItem item, DateOnly today, bool visible) =>
        visible ? PreviewPriority.Visible
        : item.Status == ItemStatus.Waiting || item.PlannedFor <= today ? PreviewPriority.WaitingOrToday
        : PreviewPriority.Rest;
}
