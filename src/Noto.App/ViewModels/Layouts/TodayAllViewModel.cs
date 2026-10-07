using CommunityToolkit.Mvvm.ComponentModel;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Insights;
using Noto.Core.Models;

namespace Noto.App.ViewModels;

// The opt-in "Today (all)" lens: one day across workspaces, each row carrying its workspace colour bar.
// Workspaces outside their focus hours drop out; capacity is summed over the ones counted in minutes.
public sealed partial class TodayAllViewModel : ItemListViewModel
{
    List<SectionViewModel> _sections = [];
    readonly Dictionary<Guid, WorkspaceSnapshot> _snapshots = [];

    public TodayAllViewModel(AppServices services)
        : base(services, Guid.Empty) { }

    public override IReadOnlyList<SectionViewModel> Sections => _sections;
    public override WorkspaceSnapshot? FocusedSnapshot =>
        FocusedRow is { } row && _snapshots.TryGetValue(row.Item.WorkspaceId, out var snap)
            ? snap
            : Snapshot;

    [ObservableProperty]
    string? _bannerText;

    [ObservableProperty]
    string? _capacityText;

    [ObservableProperty]
    string? _quietText;

    public override async Task ReloadAsync()
    {
        var keep = FocusedRow?.Id;
        var view = await Services.TodayAll.GetAsync();
        _snapshots.Clear();

        var sections = new List<SectionViewModel>();
        var committed = 0;
        var capacity = 0;
        foreach (var section in view.Sections)
        {
            var ws = section.Workspace;
            var snap = _snapshots[ws.Id] = await Services.Reader.LoadAsync(ws.Id);
            Snapshot ??= snap;
            var byId = snap.Items.ToDictionary(i => i.Id);

            var s = new SectionViewModel(ws.Name, collapsible: false);
            var v = section.View;
            var open = (v.Now is null ? [] : new[] { v.Now })
                .Concat(v.Pinned)
                .Concat(v.Planned)
                .Concat(v.Waiting);
            s.Replace(
                open.Select(i =>
                {
                    var row = Wire(
                        ItemRowFactory.Create(i, snap, snap.Workspace.NowItemId == i.Id, byId)
                    );
                    row.WorkspaceAccent = ws.Color;
                    return row;
                })
            );
            sections.Add(s);

            var summary = CapacityBar.Compute(ws, v, snap.MetricsOf, snap.FallbackMinutes);
            if (summary.Unit == CapacityUnit.Minutes)
            {
                committed += summary.Committed;
                capacity += summary.Capacity;
            }
        }
        Snapshot = _snapshots.Values.FirstOrDefault();

        _sections = sections;
        OnPropertyChanged(nameof(Sections));
        BannerText =
            view.TotalNeedsDecision == 0
                ? null
                : $"{view.TotalNeedsDecision} items carried over need a decision";
        CapacityText =
            capacity == 0
                ? null
                : $"Capacity {Duration.Short(committed)} / {Duration.Short(capacity)}"
                    + (committed > capacity ? "  · over" : "  · fits");
        QuietText =
            view.Quiet.Count == 0
                ? null
                : $"Outside focus hours: {string.Join(", ", view.Quiet.Select(w => w.Name))}";
        RestoreFocus(keep);
    }
}
