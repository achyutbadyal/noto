using Noto.Core.Derivations;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Time;

namespace Noto.Core.Workspaces;

public sealed record WorkspaceToday(
    Workspace Workspace,
    DateOnly Today,
    TodayView View,
    int NeedsDecision
);

// Opt-in lens over the single day a person lives. Workspaces outside focus hours drop out and go quiet.
public sealed record TodayAllView(
    IReadOnlyList<WorkspaceToday> Sections,
    IReadOnlyList<Workspace> Quiet
)
{
    public int TotalNeedsDecision => Sections.Sum(s => s.NeedsDecision);
}

public sealed class TodayAllService(IUnitOfWork uow, IClock clock, DerivationService derive)
{
    public async Task<TodayAllView> GetAsync()
    {
        var workspaces = await uow.RunAsync(async s =>
            (await s.Workspaces.ListAsync())
                .Where(w => w.ArchivedAt is null)
                .OrderBy(w => w.SortRank)
                .ToList()
        );

        var sections = new List<WorkspaceToday>();
        var quiet = new List<Workspace>();
        foreach (var ws in workspaces)
        {
            if (!FocusHours.IsActive(ws, clock))
            {
                quiet.Add(ws);
                continue;
            }
            var today = LogicalDate.Today(ws, clock);
            var view = await derive.GetTodayAsync(ws.Id);
            sections.Add(new WorkspaceToday(ws, today, view, view.NeedsDecision(today)));
        }
        return new TodayAllView(sections, quiet);
    }

    // Sidebar badge: items needing a decision, or 0 while the workspace is outside its focus hours.
    public async Task<int> BadgeAsync(Guid workspaceId)
    {
        var ws = await uow.RunAsync(s => s.Workspaces.GetAsync(workspaceId));
        if (ws is null || !FocusHours.IsActive(ws, clock))
            return 0;
        var view = await derive.GetTodayAsync(workspaceId);
        return view.NeedsDecision(LogicalDate.Today(ws, clock));
    }
}
