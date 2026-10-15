using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Layouts;
using Noto.Core.Models;
using Noto.Core.Text;

namespace Noto.App.ViewModels;

// The Week and Month scopes of the home screen: a month grid and a week of day columns (the kanban reading of the calendar). Dragging a
// card to another day runs the same plan command as the keyboard, so undo and the toast behave identically.
// It is not a layout: it shows whatever the workspace's layout would, spread over days, so the shell owns
// the Day / Week / Month switch and this view model only asks for a different scope (m, w).
// Status stays the single source of truth; a day is just PlannedFor.
public sealed partial class CalendarViewModel : ItemListViewModel
{
    const int MonthChips = 3;

    List<SectionViewModel> _days = [];
    List<SectionViewModel> _trays = [];

    readonly AppearanceViewModel _appearance;

    public CalendarViewModel(
        ListServices services,
        Guid workspaceId,
        AppearanceViewModel appearance
    )
        : base(services, workspaceId)
    {
        _appearance = appearance;
        appearance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AppearanceViewModel.WeekLayout))
                return;
            OnPropertyChanged(nameof(IsWeekStacked));
            OnPropertyChanged(nameof(IsWeekColumns));
            if (IsWeek)
                _ = ReloadAsync();
        };
    }

    public AppearanceViewModel Appearance => _appearance;

    public override IReadOnlyList<SectionViewModel> Sections => [.. _days, .. _trays];
    public IReadOnlyList<SectionViewModel> Days => _days;
    public IReadOnlyList<SectionViewModel> Trays => _trays;
    public IReadOnlyList<string> WeekdayHeaders { get; } =
    ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    [
        ObservableProperty,
        NotifyPropertyChangedFor(
            nameof(IsMonth),
            nameof(IsWeek),
            nameof(IsWeekStacked),
            nameof(IsWeekColumns),
            nameof(TrayVisible)
        )
    ]
    CalendarMode _mode = CalendarMode.Month;

    [ObservableProperty]
    DateOnly? _anchor;

    [ObservableProperty]
    string _title = "";

    [ObservableProperty]
    int _weekCount = 5;

    [ObservableProperty]
    bool _isOnToday = true;

    // Closed until asked for: both grids want every pixel of width, and the toolbar button counts what waits.
    bool _trayOpen;
    public bool TrayVisible => _trayOpen;

    // "Unplanned 3": what is waiting for a day, so the tray is discoverable while it is closed.
    public string TrayLabel =>
        _trays.Sum(t => t.Rows.Count) is var n and > 0 ? $"Unplanned · {n}" : "Unplanned";

    // The shell switches scope; this is raised when a key or a click inside the calendar wants another.
    public event Action<ViewScope>? ScopeRequested;

    public bool IsMonth => Mode == CalendarMode.Month;
    public bool IsWeek => Mode == CalendarMode.Week;
    public bool IsWeekStacked => IsWeek && _appearance.IsWeekVertical;
    public bool IsWeekColumns => IsWeek && _appearance.IsWeekHorizontal;

    public override async Task ReloadAsync()
    {
        var keep = FocusedRow?.Id;
        var snap = await Services.Reader.LoadAsync(WorkspaceId);
        Snapshot = snap;
        var today = snap.Today;
        var anchor = Anchor ??= today;
        var byId = snap.Items.ToDictionary(i => i.Id);
        var view = CalendarLayout.Build(snap.Items, today, anchor, Mode);

        // A day column or month chip is narrow, so rows drop the estimate/priority rail and the title keeps its room.
        ItemRowViewModel Row(TodoItem i)
        {
            var row = Wire(ItemRowFactory.Create(i, snap, snap.Workspace.NowItemId == i.Id, byId));
            row.IsCompact = !IsWeekStacked;
            return row;
        }

        _days = view
            .Days.Select(d =>
            {
                var section = new SectionViewModel(Label(d.Day, today), collapsible: false)
                {
                    Day = d.Day,
                    IsDropTarget = d.Day >= today,
                    IsToday = d.Day == today,
                    IsPast = d.Day < today,
                    IsWeekend = d.Day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                    IsOutsideMonth = Mode == CalendarMode.Month && d.Day.Month != anchor.Month,
                    MaxShown = Mode == CalendarMode.Month ? MonthChips : int.MaxValue,
                };
                section.Replace(d.Open.Select(Row).Concat(d.Done.Select(Row)));
                section.SetLoad(Load(d.Open, Mode == CalendarMode.Week));
                return section;
            })
            .ToList();

        var overdue = new SectionViewModel("Overdue", collapsible: false) { IsDropTarget = false };
        overdue.Replace(view.Overdue.Select(Row));
        var unscheduled = new SectionViewModel("Unscheduled", collapsible: false)
        {
            IsDropTarget = true,
        };
        unscheduled.Replace(view.Unscheduled.Select(Row));
        _trays = [overdue, unscheduled];

        WeekCount = CalendarLayout.GridWeeks(anchor, Mode);
        IsOnToday =
            Mode == CalendarMode.Month
                ? anchor.Year == today.Year && anchor.Month == today.Month
                : CalendarLayout.WeekStart(anchor) == CalendarLayout.WeekStart(today);
        Title = Heading(anchor, view.Start);

        OnPropertyChanged(nameof(Days));
        OnPropertyChanged(nameof(Trays));
        OnPropertyChanged(nameof(TrayLabel));
        NotifySectionsChanged();
        RestoreFocus(keep);
    }

    static string Label(DateOnly day, DateOnly today) =>
        day == today ? "Today" : day.ToString("ddd d", CultureInfo.InvariantCulture);

    // A month cell is narrow, so it only counts; a week column adds the estimates that are known.
    static string Load(IReadOnlyList<TodoItem> open, bool withTime)
    {
        if (open.Count == 0)
            return "";
        var minutes = open.Sum(i => i.EstimateMinutes ?? 0);
        return withTime && minutes > 0
            ? $"{open.Count} · {Duration.Short(minutes)}"
            : $"{open.Count}";
    }

    string Heading(DateOnly anchor, DateOnly start)
    {
        if (Mode == CalendarMode.Month)
            return anchor.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var end = start.AddDays(6);
        var from = start.ToString("MMM d", CultureInfo.InvariantCulture);
        var to =
            start.Month == end.Month
                ? end.Day.ToString(CultureInfo.InvariantCulture)
                : end.ToString("MMM d", CultureInfo.InvariantCulture);
        return $"{from} – {to}, {end.Year}";
    }

    [RelayCommand]
    async Task StepAsync(string direction)
    {
        var sign = direction == "prev" ? -1 : 1;
        var from = Anchor ?? TodayOrFallback;
        Anchor = Mode == CalendarMode.Month ? from.AddMonths(sign) : from.AddDays(7 * sign);
        await ReloadAsync();
    }

    [RelayCommand]
    void ToggleTray()
    {
        _trayOpen = !_trayOpen;
        OnPropertyChanged(nameof(TrayVisible));
    }

    [RelayCommand]
    async Task GoTodayAsync()
    {
        Anchor = TodayOrFallback;
        await ReloadAsync();
    }

    // Opening a day from the month grid shows its week.
    public void OpenWeek(DateOnly day)
    {
        Anchor = day;
        ScopeRequested?.Invoke(ViewScope.Week);
    }

    // Plans `item` for `day`; a null day is the Unscheduled tray. Same path for a drag and for the keyboard.
    public async Task MoveToDayAsync(TodoItem item, DateOnly? day)
    {
        var today = TodayOrFallback;
        if (!CalendarMoves.CanDropOn(day, today))
        {
            Say("That day has passed. Pick today or later.");
            return;
        }
        var commands = CalendarMoves.Plan(item, day, today);
        if (commands.Count == 0)
        {
            if (day is null && item.PlannedFor is null && item.DueDate is not null)
                Say("This task is only dated by its due date, so it stays on that day.");
            return;
        }
        var where =
            day is null ? "Unscheduled"
            : day == today ? "today"
            : day.Value.ToString("ddd MMM d", CultureInfo.InvariantCulture);
        await Services.Runner.RunAllAsync(commands, $"Moved “{item.Title}” to {where}");
    }

    void Say(string text) => Message = Decisions.Message = text;

    int DayIndexOf(ItemRowViewModel? row) =>
        row is null ? -1 : _days.FindIndex(d => d.Rows.Contains(row));

    // ←/→ and h/l hop to the neighbouring day that has a card; ↑/↓ and j/k step inside a day.
    // ⇧←/⇧→ move the card a day, ⇧↑/⇧↓ a week. PageUp/PageDown page, Home returns to today, m/w switch scope.
    public override async Task<bool> HandleKeyAsync(KeyChord chord)
    {
        if (!IsEditingTitle && Decisions.Prompt is null && !chord.Command)
        {
            switch (chord)
            {
                case { Key: "ArrowRight" or "l", Shift: true }:
                    return await NudgeAsync(1);
                case { Key: "ArrowLeft" or "h", Shift: true }:
                    return await NudgeAsync(-1);
                case { Key: "ArrowDown" or "j", Shift: true } when Mode == CalendarMode.Month:
                    return await NudgeAsync(7);
                case { Key: "ArrowUp" or "k", Shift: true } when Mode == CalendarMode.Month:
                    return await NudgeAsync(-7);
                case { Key: "ArrowRight" or "l", Shift: false }:
                    FocusDay(1);
                    return true;
                case { Key: "ArrowLeft" or "h", Shift: false }:
                    FocusDay(-1);
                    return true;
                case { Key: "PageDown", Shift: false }:
                    await StepAsync("next");
                    return true;
                case { Key: "PageUp", Shift: false }:
                    await StepAsync("prev");
                    return true;
                case { Key: "Home", Shift: false }:
                    await GoTodayAsync();
                    return true;
                case { Key: "u", Shift: false }:
                    ToggleTray();
                    return true;
                case { Key: "m", Shift: false }:
                    ScopeRequested?.Invoke(ViewScope.Month);
                    return true;
                case { Key: "w", Shift: false } when Mode == CalendarMode.Month:
                    ScopeRequested?.Invoke(ViewScope.Week);
                    return true;
            }
        }
        return await base.HandleKeyAsync(chord);
    }

    async Task<bool> NudgeAsync(int days)
    {
        if (FocusedRow is not { } row)
            return true;
        var current = row.Item.PlannedFor ?? row.Item.DueDate ?? TodayOrFallback;
        await MoveToDayAsync(row.Item, current.AddDays(days));
        return true;
    }

    void FocusDay(int delta)
    {
        if (FocusedRow is null)
        {
            SetFocus(Sections.SelectMany(s => s.Rows).FirstOrDefault());
            return;
        }
        var from = DayIndexOf(FocusedRow);
        if (from < 0)
            return;
        var rowIndex = _days[from].Rows.IndexOf(FocusedRow);
        for (var d = from + delta; d >= 0 && d < _days.Count; d += delta)
        {
            if (_days[d].Rows.Count == 0)
                continue;
            SetFocus(_days[d].Rows[Math.Min(rowIndex, _days[d].Rows.Count - 1)]);
            return;
        }
    }
}
