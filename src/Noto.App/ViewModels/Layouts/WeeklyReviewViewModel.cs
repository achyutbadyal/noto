using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Commands;
using Noto.Core.Insights;
using Noto.Core.Layouts;
using Noto.Core.Models;
using Noto.Core.Recurrence;

namespace Noto.App.ViewModels;

public enum SweepChoice
{
    Keep,
    Promote,
    Drop,
}

public sealed partial class SweepEntryViewModel(SomedayEntry entry) : ObservableObject
{
    public SomedayEntry Entry { get; } = entry;
    public string Title => Entry.Item.Title;
    public string Age => $"untouched {Entry.DaysUntouched}d";

    [ObservableProperty]
    SweepChoice _choice = entry.PreselectDrop ? SweepChoice.Drop : SweepChoice.Keep;

    [RelayCommand]
    void Pick(string choice) => Choice = Enum.Parse<SweepChoice>(choice);

    public bool IsKeep => Choice == SweepChoice.Keep;
    public bool IsPromote => Choice == SweepChoice.Promote;
    public bool IsDrop => Choice == SweepChoice.Drop;

    partial void OnChoiceChanged(SweepChoice value)
    {
        OnPropertyChanged(nameof(IsKeep));
        OnPropertyChanged(nameof(IsPromote));
        OnPropertyChanged(nameof(IsDrop));
    }
}

public enum WeeklyStep
{
    Wins,
    Someday,
    Stuck,
    Estimates,
    NextWeek,
}

// Guided ~5 minute reflection (docs/07 §8.1): wins, someday sweep, stuck patterns, estimate check, next week.
public sealed partial class WeeklyReviewViewModel(AppServices services, Guid workspaceId)
    : ObservableObject
{
    DateOnly _weekStart;

    public ObservableCollection<ItemRowViewModel> Wins { get; } = [];
    public ObservableCollection<SweepEntryViewModel> Sweep { get; } = [];
    public ObservableCollection<InsightRow> StuckRows { get; } = [];
    public ObservableCollection<InsightRow> EstimateRows { get; } = [];

    [
        ObservableProperty,
        NotifyPropertyChangedFor(nameof(StepTitle), nameof(StepNumber), nameof(IsLast))
    ]
    WeeklyStep _step;

    [ObservableProperty]
    string? _finallyText;

    [ObservableProperty]
    string? _stuckSummary;

    [ObservableProperty]
    string? _stuckSuggestion;

    [ObservableProperty]
    string _outcome1 = "";

    [ObservableProperty]
    string _outcome2 = "";

    [ObservableProperty]
    string _outcome3 = "";

    [ObservableProperty]
    string _heading = "";

    public event Action? Finished;

    public int StepNumber => (int)Step + 1;
    public bool IsLast => Step == WeeklyStep.NextWeek;
    public string StepTitle =>
        Step switch
        {
            WeeklyStep.Wins => $"Wins ({Wins.Count})",
            WeeklyStep.Someday => $"Someday sweep ({Sweep.Count})",
            WeeklyStep.Stuck => "Stuck patterns",
            WeeklyStep.Estimates => "Estimate check",
            _ => "Next week",
        };

    public async Task LoadAsync()
    {
        var snap = await services.Reader.LoadAsync(workspaceId);
        var records = await services.Reader.RecordsAsync(workspaceId);
        _weekStart = RRule.MondayOf(snap.Today);
        var outcomes = await services.DayNoteService.GetOutcomesAsync(
            workspaceId,
            _weekStart.AddDays(7)
        );
        var data = WeeklyReviewBuilder.Build(
            snap.Workspace,
            records,
            snap.Today,
            _weekStart,
            outcomes
        );

        Heading =
            $"Week of {_weekStart.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture)}";
        var byId = snap.Items.ToDictionary(i => i.Id);
        Wins.Clear();
        foreach (var win in data.Wins)
            Wins.Add(ItemRowFactory.Create(win.Item, snap, false, byId));
        FinallyText = data.Finally is { Age: > 0 } f
            ? $"Finally: {f.Item.Title}, after {f.Age} days"
            : null;

        Sweep.Clear();
        foreach (var entry in data.SomedaySweep)
            Sweep.Add(new SweepEntryViewModel(entry));

        StuckRows.Clear();
        var total = Math.Max(1, data.Stuck.Mix.Values.Sum());
        foreach (var (reason, count) in data.Stuck.Mix.OrderByDescending(kv => kv.Value))
            StuckRows.Add(
                new InsightRow(reason.Replace('_', ' '), (double)count / total, count.ToString())
            );
        StuckSummary = data.Stuck.TopReason is { } top
            ? $"Most of your stuck items this month were “{top.Replace('_', ' ')}”."
            : "Nothing got stuck this month.";
        StuckSuggestion = data.Stuck.Suggestion?.Prompt;

        EstimateRows.Clear();
        foreach (var a in data.EstimateCheck)
            EstimateRows.Add(
                new InsightRow(
                    a.Bucket switch
                    {
                        SizeBucket.Small => "≤30m",
                        SizeBucket.Medium => "30m–2h",
                        _ => "≥2h",
                    },
                    Math.Min(1, a.MedianRatio / 2),
                    $"takes {a.MedianRatio:0.0}× the estimate"
                )
            );

        var list = data.NextWeekOutcomes;
        (Outcome1, Outcome2, Outcome3) = (
            list.ElementAtOrDefault(0) ?? "",
            list.ElementAtOrDefault(1) ?? "",
            list.ElementAtOrDefault(2) ?? ""
        );
        Step = WeeklyStep.Wins;
        OnPropertyChanged(nameof(StepTitle));
    }

    [RelayCommand]
    public void Next()
    {
        if (!IsLast)
            Step++;
    }

    [RelayCommand]
    public void Back()
    {
        if (Step > WeeklyStep.Wins)
            Step--;
    }

    // Applies the sweep as one undoable action and pins the top-3 for next week.
    [RelayCommand]
    public async Task FinishAsync()
    {
        var commands = new List<ItemCommand>();
        foreach (var s in Sweep)
        {
            if (s.Choice == SweepChoice.Promote)
                commands.Add(new PlanItem(s.Entry.Item.Id, null, PlanKind.KeepToday));
            else if (s.Choice == SweepChoice.Drop)
                commands.Add(new DropItem(s.Entry.Item.Id, DropReason.NotWorthIt));
        }
        if (commands.Count > 0)
            await services.Runner.RunAllAsync(commands, "Weekly review");

        await services.DayNoteService.SetOutcomesAsync(
            workspaceId,
            _weekStart.AddDays(7),
            [Outcome1, Outcome2, Outcome3]
        );
        Finished?.Invoke();
    }

    public async Task<bool> HandleKeyAsync(KeyChord chord)
    {
        switch (chord.Key)
        {
            case "ArrowRight" when Step != WeeklyStep.NextWeek:
                Next();
                return true;
            case "ArrowLeft":
                Back();
                return true;
            case "Escape":
                Finished?.Invoke();
                return true;
            case "Enter" when IsLast && Step == WeeklyStep.NextWeek && !chord.Shift:
                return false; // Enter inside the outcome boxes
            default:
                return false;
        }
    }
}
