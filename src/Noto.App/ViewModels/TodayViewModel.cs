using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Commands;
using Noto.Core.Insights;
using Noto.Core.Models;

namespace Noto.App.ViewModels;

public sealed partial class TodayViewModel : ItemListViewModel
{
    readonly SectionViewModel _now = new("Now", collapsible: false);
    readonly SectionViewModel _planned = new("Planned");
    readonly SectionViewModel _waiting = new("Waiting on");
    readonly SectionViewModel _done = new("Done today") { IsCollapsed = true };

    public TodayViewModel(AppServices services, Guid workspaceId, Func<string, Guid?>? resolveWorkspace = null)
        : base(services, workspaceId)
    {
        Sections = [_now, _planned, _waiting, _done];
        Add = new AddItemViewModel(services, workspaceId, plannedForToday: true, resolveWorkspace, () => TodayOrFallback);
        Strip = new DayStripViewModel();
    }

    public override IReadOnlyList<SectionViewModel> Sections { get; }
    public AddItemViewModel Add { get; }
    public DayStripViewModel Strip { get; }

    [ObservableProperty] CapacityViewModel? _capacity;
    [ObservableProperty] string? _forecastText;
    [ObservableProperty] string? _bannerText;
    [ObservableProperty] int _needsDecision;
    [ObservableProperty] bool _isEmpty;

    public string EmptyText => "Nothing planned. Pull from Someday or add something.";

    public override async Task ReloadAsync()
    {
        var keep = FocusedRow?.Id;
        var snap = await Services.Reader.LoadAsync(WorkspaceId);
        Snapshot = snap;
        var view = snap.Today_;
        var byId = snap.Items.ToDictionary(i => i.Id);
        ItemRowViewModel Row(TodoItem i, bool now = false) => Wire(ItemRowFactory.Create(i, snap, now, byId));

        _now.Replace(view.Now is null ? [] : [Row(view.Now, now: true)]);
        _planned.Replace(view.Pinned.Concat(view.Planned).Select(i => Row(i)));
        _waiting.Replace(view.Waiting.Select(i => Row(i)));
        _done.Replace(view.DoneToday.Select(i => Row(i)));

        NeedsDecision = snap.NeedsDecision.Count;
        BannerText = NeedsDecision == 0 ? null : $"{NeedsDecision} item{(NeedsDecision == 1 ? "" : "s")} carried over need a decision";
        IsEmpty = _now.Rows.Count + _planned.Rows.Count + _waiting.Rows.Count == 0;

        var committed = view.Pinned.Concat(view.Planned).ToList();
        Capacity = new CapacityViewModel(
            CapacityBar.Compute(snap.Workspace, view, snap.MetricsOf, snap.FallbackMinutes),
            snap.Workspace.CapacityUnit == CapacityUnit.Minutes && committed.Any(i => i.EstimateMinutes is null));

        var history = await Services.Reader.DayStatsAsync(WorkspaceId, snap.Today.AddDays(-60), snap.Today.AddDays(-1));
        Strip.Load(snap.Today, history, selected: null);
        var forecast = PaceForecaster.Forecast(snap.Today, committed.Count + (view.Now is null ? 0 : 1), history);
        ForecastText = forecast is null ? null
            : $"On days like this you usually finish {forecast.ExpectedDone} of {forecast.Planned}. Expect {forecast.ExpectedCarry} to carry.";

        RestoreFocus(keep);
    }

    [RelayCommand]
    async Task ApplyDeferAsync() => await ApplyDeferSuggestionAsync();

    // Applies the capacity bar's suggestion: defer the proposed items to tomorrow.
    public async Task ApplyDeferSuggestionAsync()
    {
        if (Capacity is not { HasSuggestion: true } cap || Snapshot is not { } snap) return;
        var tomorrow = snap.Today.AddDays(1);
        await Services.Runner.RunAllAsync(
            cap.SuggestedDefers.Select(i => (ItemCommand)new PlanItem(i.Id, tomorrow, PlanKind.Defer)).ToList(),
            $"Deferred {cap.SuggestedDefers.Count} to tomorrow");
    }
}
