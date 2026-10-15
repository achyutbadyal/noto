using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Commands;
using Noto.Core.Insights;
using Noto.Core.Layouts;
using Noto.Core.Models;
using Noto.Core.Text;

namespace Noto.App.ViewModels;

// One part of the day as a bar: how much is planned there, against the busiest part, so the shape of the
// day reads at a glance and an empty part says it is free.
public sealed record PartBar(string Label, string Amount, double Fraction, bool IsEmpty);

// A future day as a plan, not a list: how heavy the day is, how its work is spread over the parts of the
// day, what is due on it, and what could still go there. Every row is a real item, so the inspector, the
// hover preview and the single-key decisions all work here exactly as they do on Today. (docs/07 §8.3)
public sealed partial class DayAheadViewModel : ItemListViewModel
{
    List<SectionViewModel> _sections = [];

    public DayAheadViewModel(
        ListServices services,
        Guid workspaceId,
        Func<string, Guid?>? resolveWorkspace = null
    )
        : base(services, workspaceId)
    {
        Add = new AddItemViewModel(
            services.Runner,
            workspaceId,
            plannedForToday: true,
            resolveWorkspace,
            () => Day,
            services.Suggestions,
            services.Links
        );
    }

    public override IReadOnlyList<SectionViewModel> Sections => _sections;
    public AddItemViewModel Add { get; }
    public ObservableCollection<PartBar> Parts { get; } = [];

    [ObservableProperty]
    DateOnly _day;

    // How a sentence names the day: "tomorrow", "Saturday".
    [ObservableProperty]
    string _dayName = "";

    [ObservableProperty]
    string _headline = "";

    [ObservableProperty]
    string _verdict = "";

    [ObservableProperty]
    double _loadPercent;

    [ObservableProperty]
    bool _isOver;

    [ObservableProperty]
    bool _isEmpty;

    [ObservableProperty]
    bool _hasLoad;

    [ObservableProperty]
    string? _forecastText;

    [ObservableProperty]
    string? _footnote;

    [ObservableProperty]
    string _emptyText = "";

    public string WorkspaceName => Snapshot?.Workspace.Name ?? "";

    // The page heading, so the screen never needs a second one to say which day it is.
    public string HeaderTitle =>
        DayAheadText.HeaderTitle(Day, Snapshot?.Today ?? DateOnly.FromDateTime(DateTime.Today));

    public async Task LoadAsync(DateOnly day)
    {
        Day = day;
        await ReloadAsync();
    }

    public override async Task ReloadAsync()
    {
        var keep = FocusedRow?.Id;
        var snap = await Services.Reader.LoadAsync(WorkspaceId);
        Snapshot = snap;
        OnPropertyChanged(nameof(WorkspaceName));
        OnPropertyChanged(nameof(HeaderTitle));

        var byId = snap.Items.ToDictionary(i => i.Id);
        var plan = DayAheadLayout.Build(
            snap.Items,
            snap.Today,
            Day,
            snap.Workspace,
            snap.FallbackMinutes,
            i => snap.MetricsOf(i).Carry
        );

        ItemRowViewModel Row(TodoItem item, Action<ItemRowViewModel>? tweak = null)
        {
            var row = Wire(
                ItemRowFactory.Create(item, snap, snap.Workspace.NowItemId == item.Id, byId)
            );
            row.ShowDetail = true;
            // The section already says "Morning", so the row does not.
            row.Chips = [.. row.Chips.Where(c => c.Kind != "part")];
            tweak?.Invoke(row);
            return row;
        }

        var sections = new List<SectionViewModel>();
        foreach (var group in plan.Parts)
        {
            var section = new SectionViewModel(
                DayAheadText.PartName(group.Part),
                collapsible: false
            )
            {
                HeaderDetail =
                    group.Part is null && plan.Parts.Count == 1
                        ? null
                        : DayAheadText.Amount(group.Cost, plan.Unit),
            };
            section.Replace(group.Items.Select(i => Row(i)));
            sections.Add(section);
        }
        AddSection(sections, "Waiting on", plan.Waiting.Select(i => Row(i)));
        AddSection(sections, "Due this day", plan.DueOnly.Select(i => Row(i)));
        var suggestions = new SectionViewModel("Could go here", collapsible: false)
        {
            OffersPlan = true,
        };
        suggestions.Replace(
            plan.Suggestions.Select(s =>
                Row(
                    s.Item,
                    r =>
                    {
                        r.OfferPlan = true;
                        // The reason is already on the right; a second "Due Mon" chip would repeat it.
                        r.Chips = [.. r.Chips.Where(c => !c.IsDue && !c.IsLate)];
                        r.ExtraText = DayAheadText.ReasonText(s, snap.Today);
                    }
                )
            )
        );
        if (!suggestions.IsEmpty)
            sections.Add(suggestions);
        _sections = sections;

        DayName = DayAheadText.Name(Day, snap.Today);
        Headline = DayAheadText.Headline(plan);
        Verdict = DayAheadText.Verdict(plan);
        IsOver = plan.IsOver;
        HasLoad = plan.PlannedCount > 0;
        LoadPercent =
            plan.Capacity == 0 ? 0 : Math.Min(100, 100.0 * plan.Committed / plan.Capacity);
        Footnote =
            plan.Unestimated > 0
                ? $"{plan.Unestimated} without an estimate counted as {Duration.Short(snap.FallbackMinutes)}, your median"
                : null;
        IsEmpty = plan.IsEmpty;
        EmptyText =
            plan.Suggestions.Count > 0
                ? $"Nothing is planned for {DayName} yet. Start from what is waiting below, or add something."
                : $"Nothing is planned for {DayName}, and everything else already has a day. Add something.";

        LoadParts(plan);
        ForecastText = await ForecastAsync(snap, plan);

        NotifySectionsChanged();
        RestoreFocus(keep);
    }

    static void AddSection(
        List<SectionViewModel> sections,
        string title,
        IEnumerable<ItemRowViewModel> rows
    )
    {
        var section = new SectionViewModel(title, collapsible: false);
        section.Replace(rows);
        if (!section.IsEmpty)
            sections.Add(section);
    }

    // The four parts of the day, plus whatever has no time. A part with nothing in it is drawn empty so the
    // free stretches show, which is what you look at when deciding where something fits.
    void LoadParts(DayAheadPlan plan)
    {
        var costs = new (string Label, int Cost)[]
        {
            ("Morning", CostOf(plan, TimeOfDay.Morning)),
            ("Midday", CostOf(plan, TimeOfDay.Midday)),
            ("Afternoon", CostOf(plan, TimeOfDay.Afternoon)),
            ("Evening", CostOf(plan, TimeOfDay.Evening)),
            ("Any time", CostOf(plan, null)),
        };
        var peak = Math.Max(1, costs.Max(c => c.Cost));
        Parts.Clear();
        foreach (var (label, cost) in costs)
            Parts.Add(
                new PartBar(
                    label,
                    cost == 0 ? "Free" : DayAheadText.Amount(cost, plan.Unit),
                    (double)cost / peak,
                    cost == 0
                )
            );
    }

    static int CostOf(DayAheadPlan plan, TimeOfDay? part) =>
        plan.Parts.FirstOrDefault(p => p.Part == part)?.Cost ?? 0;

    async Task<string?> ForecastAsync(WorkspaceSnapshot snap, DayAheadPlan plan)
    {
        if (plan.PlannedCount == 0)
            return null;
        var history = await Services.Reader.DayStatsAsync(
            WorkspaceId,
            snap.Today.AddDays(-60),
            snap.Today.AddDays(-1)
        );
        var forecast = PaceForecaster.Forecast(Day, plan.PlannedCount, history);
        return forecast is null
            ? null
            : $"On days like this you usually finish {forecast.ExpectedDone} of {forecast.Planned}. Expect {forecast.ExpectedCarry} to carry.";
    }

    [RelayCommand]
    async Task PlanHereAsync(ItemRowViewModel? row)
    {
        if (row is null)
            return;
        await Services.Runner.RunAllAsync(
            [new PlanItem(row.Id, Day, PlanKind.Plan)],
            $"Planned “{row.Title}” for {DayName}"
        );
    }
}
