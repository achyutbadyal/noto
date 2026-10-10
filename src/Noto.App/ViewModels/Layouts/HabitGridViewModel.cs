using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Commands;
using Noto.Core.Habits;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Recurrence;

namespace Noto.App.ViewModels;

public sealed record HabitCellViewModel(
    DateOnly Day,
    HabitDayState State,
    bool IsToday,
    Guid RuleId,
    bool IsEditable
)
{
    // A missed day is an empty square, never a red cross (docs/07 §9.1). Rendered as a rounded
    // square in the view, so the state is exposed rather than a text glyph.
    public bool IsDone => State == HabitDayState.Done;
    public bool IsMissed => State is HabitDayState.Missed or HabitDayState.Pending;
    public bool IsScheduled => State is not HabitDayState.NotScheduled;
    public string Description =>
        $"{Day.ToString("dddd", System.Globalization.CultureInfo.InvariantCulture)}: "
        + State switch
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

    [ObservableProperty]
    bool _isFocused;

    public string AutomationName =>
        $"{Name}, {StreakText}, " + string.Join(", ", Cells.Select(c => c.Description));
}

// Habit layout: recurrence rules with missed_behavior = skip as a week grid with flex streaks. Other items sit
// under "Not habits (n)", one click away (docs/03).
public sealed partial class HabitGridViewModel : ItemListViewModel
{
    readonly SectionViewModel _notHabits = new("Not habits") { IsCollapsed = true };
    int _focusedHabit;

    readonly IUnitOfWork _uow;
    readonly RecurrenceService _recurrence;

    public HabitGridViewModel(
        ListServices services,
        IUnitOfWork uow,
        RecurrenceService recurrence,
        Guid workspaceId
    )
        : base(services, workspaceId)
    {
        _uow = uow;
        _recurrence = recurrence;
    }

    public override IReadOnlyList<SectionViewModel> Sections => [_notHabits];
    public ObservableCollection<HabitRowViewModel> Habits { get; } = [];
    public IReadOnlyList<string> DayLabels { get; private set; } = [];

    [ObservableProperty]
    bool _hasNoHabits;

    public override async Task ReloadAsync()
    {
        var keep = FocusedRow?.Id;
        await _recurrence.GenerateDueAsync(WorkspaceId);
        var snap = await Services.Reader.LoadAsync(WorkspaceId);
        Snapshot = snap;
        var byId = snap.Items.ToDictionary(i => i.Id);
        var weekStart = RRule.MondayOf(snap.Today);
        DayLabels = Enumerable
            .Range(0, 7)
            .Select(n =>
                weekStart
                    .AddDays(n)
                    .ToString("ddd", System.Globalization.CultureInfo.InvariantCulture)[..1]
            )
            .ToList();

        var rules = (await _uow.RunAsync(s => s.Rules.ListAsync(WorkspaceId)))
            .Where(r => r.DeletedAt is null && r.MissedBehavior == MissedBehavior.Skip)
            .OrderBy(r => r.Template.Title)
            .ToList();

        Habits.Clear();
        foreach (var rule in rules)
        {
            var instances = snap.Items.Where(i => i.RecurrenceRuleId == rule.Id).ToList();
            var stats = HabitCalculator.Compute(rule, instances, snap.Today, weekStart.AddDays(-0));
            var week = Enumerable
                .Range(0, 7)
                .Select(n => weekStart.AddDays(n))
                .Select(day =>
                {
                    var state =
                        stats.Heatmap.FirstOrDefault(c => c.Day == day)?.State
                        ?? HabitDayState.NotScheduled;
                    // Any day up to today can be ticked; the future cannot (docs/07 §9.1).
                    var editable = state is not HabitDayState.NotScheduled && day <= snap.Today;
                    return new HabitCellViewModel(day, state, day == snap.Today, rule.Id, editable);
                })
                .ToList();

            Habits.Add(
                new HabitRowViewModel
                {
                    RuleId = rule.Id,
                    Name = rule.Template.Title,
                    Cells = week,
                    StreakText = $"streak {stats.CurrentStreak} (best {stats.LongestStreak})",
                }
            );
        }
        HasNoHabits = Habits.Count == 0;
        _focusedHabit = Math.Clamp(_focusedHabit, 0, Math.Max(0, Habits.Count - 1));
        SetHabitFocus();

        var habitIds = rules.Select(r => r.Id).ToHashSet();
        _notHabits.Replace(
            snap.Items.Where(i =>
                    i.Status == ItemStatus.Open
                    && !i.IsContainer
                    && (i.RecurrenceRuleId is null || !habitIds.Contains(i.RecurrenceRuleId.Value))
                )
                .Select(i => Wire(ItemRowFactory.Create(i, snap, false, byId)))
        );
        NotifySectionsChanged();
        RestoreFocus(keep);
    }

    void SetHabitFocus()
    {
        for (var i = 0; i < Habits.Count; i++)
            Habits[i].IsFocused = i == _focusedHabit;
    }

    public override async Task<bool> HandleKeyAsync(KeyChord chord)
    {
        if (
            Habits.Count > 0
            && Decisions.Prompt is null
            && !IsEditingTitle
            && !_notHabits.Rows.Any(r => r.IsFocused)
        )
        {
            switch (chord.Key)
            {
                case "j" or "ArrowDown":
                    _focusedHabit = Math.Min(_focusedHabit + 1, Habits.Count - 1);
                    SetHabitFocus();
                    return true;
                case "k" or "ArrowUp":
                    _focusedHabit = Math.Max(_focusedHabit - 1, 0);
                    SetHabitFocus();
                    return true;
                case "x" or "Enter":
                    if (Habits[_focusedHabit].Cells.FirstOrDefault(c => c.IsToday) is { } today)
                        await ToggleCellAsync(today);
                    return true;
            }
        }
        return await base.HandleKeyAsync(chord);
    }

    // Any scheduled day up to today can be ticked. A day with no instance yet (a missed skip-rule day)
    // is materialised first, so the grid can be corrected after the fact (docs/07 §9.1).
    [RelayCommand]
    public async Task ToggleCellAsync(HabitCellViewModel cell)
    {
        if (!cell.IsEditable)
            return;
        var habit = Habits.FirstOrDefault(h => h.RuleId == cell.RuleId);
        if (habit is null)
            return;

        var item = await _recurrence.EnsureOccurrenceAsync(cell.RuleId, cell.Day);
        if (item is null)
            return;

        var done = item.Status == ItemStatus.Done;
        ItemCommand command = done ? new ReopenItem(item.Id) : new CompleteItem(item.Id);
        await Services.Runner.RunAsync(
            command,
            done ? $"Unticked {habit.Name}" : $"Ticked {habit.Name}"
        );
    }
}
