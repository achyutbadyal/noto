using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Commands;
using Noto.Core.Habits;
using Noto.Core.Models;
using Noto.Core.Recurrence;

namespace Noto.App.ViewModels;

public sealed record HabitCellViewModel(DateOnly Day, HabitDayState State, bool IsToday)
{
    // A missed day is an empty square, never a red cross (docs/07 §9.1).
    public string Glyph => State switch { HabitDayState.Done => "■", HabitDayState.Missed or HabitDayState.Pending => "□", _ => "·" };
    public string Description => $"{Day.ToString("dddd", System.Globalization.CultureInfo.InvariantCulture)}: " + State switch
    {
        HabitDayState.Done => "done",
        HabitDayState.Missed => "missed",
        HabitDayState.Pending => "to do today",
        _ => "not scheduled",
    };
}

public sealed partial class HabitRowViewModel : ObservableObject
{
    public required Guid RuleId { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<HabitCellViewModel> Cells { get; init; }
    public required string StreakText { get; init; }
    public Guid? TodayInstanceId { get; init; }
    public bool TodayDone { get; init; }
    [ObservableProperty] bool _isFocused;

    public string AutomationName => $"{Name}, {StreakText}, " + string.Join(", ", Cells.Select(c => c.Description));
}

// Habit layout: recurrence rules with missed_behavior = skip as a week grid with flex streaks. Other items sit
// under "Not habits (n)", one click away (docs/03).
public sealed partial class HabitGridViewModel : ItemListViewModel
{
    readonly SectionViewModel _notHabits = new("Not habits") { IsCollapsed = true };
    int _focusedHabit;

    public HabitGridViewModel(AppServices services, Guid workspaceId) : base(services, workspaceId) { }

    public override IReadOnlyList<SectionViewModel> Sections => [_notHabits];
    public ObservableCollection<HabitRowViewModel> Habits { get; } = [];
    public IReadOnlyList<string> DayLabels { get; private set; } = [];
    [ObservableProperty] bool _hasNoHabits;

    public override async Task ReloadAsync()
    {
        var keep = FocusedRow?.Id;
        await Services.Recurrence.GenerateDueAsync(WorkspaceId);
        var snap = await Services.Reader.LoadAsync(WorkspaceId);
        Snapshot = snap;
        var byId = snap.Items.ToDictionary(i => i.Id);
        var weekStart = RRule.MondayOf(snap.Today);
        DayLabels = Enumerable.Range(0, 7).Select(n => weekStart.AddDays(n).ToString("ddd", System.Globalization.CultureInfo.InvariantCulture)[..1]).ToList();

        var rules = (await Services.Uow.RunAsync(s => s.Rules.ListAsync(WorkspaceId)))
            .Where(r => r.DeletedAt is null && r.MissedBehavior == MissedBehavior.Skip).OrderBy(r => r.Template.Title).ToList();

        Habits.Clear();
        foreach (var rule in rules)
        {
            var instances = snap.Items.Where(i => i.RecurrenceRuleId == rule.Id).ToList();
            var stats = HabitCalculator.Compute(rule, instances, snap.Today, weekStart.AddDays(-0));
            var week = Enumerable.Range(0, 7).Select(n => weekStart.AddDays(n)).Select(day =>
                stats.Heatmap.FirstOrDefault(c => c.Day == day)?.State is { } state
                    ? new HabitCellViewModel(day, state, day == snap.Today)
                    : new HabitCellViewModel(day, HabitDayState.NotScheduled, day == snap.Today)).ToList();
            var todayInstance = instances.FirstOrDefault(i => i.OccurrenceDate == snap.Today);

            Habits.Add(new HabitRowViewModel
            {
                RuleId = rule.Id,
                Name = rule.Template.Title,
                Cells = week,
                StreakText = $"streak {stats.CurrentStreak} (best {stats.LongestStreak})",
                TodayInstanceId = todayInstance?.Id,
                TodayDone = todayInstance?.Status == ItemStatus.Done,
            });
        }
        HasNoHabits = Habits.Count == 0;
        _focusedHabit = Math.Clamp(_focusedHabit, 0, Math.Max(0, Habits.Count - 1));
        SetHabitFocus();

        var habitIds = rules.Select(r => r.Id).ToHashSet();
        _notHabits.Replace(snap.Items.Where(i => i.Status == ItemStatus.Open && !i.IsContainer && (i.RecurrenceRuleId is null || !habitIds.Contains(i.RecurrenceRuleId.Value)))
            .Select(i => Wire(ItemRowFactory.Create(i, snap, false, byId))));
        OnPropertyChanged(nameof(Sections));
        RestoreFocus(keep);
    }

    void SetHabitFocus()
    {
        for (var i = 0; i < Habits.Count; i++) Habits[i].IsFocused = i == _focusedHabit;
    }

    public override async Task<bool> HandleKeyAsync(KeyChord chord)
    {
        if (Habits.Count > 0 && Decisions.Prompt is null && !IsEditingTitle && !_notHabits.Rows.Any(r => r.IsFocused))
        {
            switch (chord.Key)
            {
                case "j" or "ArrowDown": _focusedHabit = Math.Min(_focusedHabit + 1, Habits.Count - 1); SetHabitFocus(); return true;
                case "k" or "ArrowUp": _focusedHabit = Math.Max(_focusedHabit - 1, 0); SetHabitFocus(); return true;
                case "x" or "Enter": await ToggleTodayAsync(Habits[_focusedHabit]); return true;
            }
        }
        return await base.HandleKeyAsync(chord);
    }

    // Only today's instance exists for a skip rule, so only today's cell can be ticked (docs/04 §2.5).
    [RelayCommand]
    public async Task ToggleTodayAsync(HabitRowViewModel habit)
    {
        if (habit.TodayInstanceId is not { } id) return;
        ItemCommand command = habit.TodayDone ? new ReopenItem(id) : new CompleteItem(id);
        await Services.Runner.RunAsync(command, habit.TodayDone ? $"Unticked {habit.Name}" : $"Ticked {habit.Name}");
    }
}
