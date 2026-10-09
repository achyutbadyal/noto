using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Commands;
using Noto.Core.Derivations;
using Noto.Core.Insights;
using Noto.Core.Models;

namespace Noto.App.ViewModels;

public sealed record StuckReasonOption(StuckReason Reason, string Label);

// Details for the focused item: the three numbers, the stuck prompt, subtasks and the life of the item (docs/07 §10.2).
public sealed partial class InspectorViewModel : ObservableObject
{
    public static readonly IReadOnlyList<StuckReasonOption> ReasonOptions =
    [
        new(StuckReason.TooBig, "Too big"),
        new(StuckReason.Blocked, "Blocked"),
        new(StuckReason.Unclear, "Unclear what to do"),
        new(StuckReason.DontWant, "Don't want to"),
        new(StuckReason.NotNeeded, "Not needed"),
    ];

    readonly WorkspaceReader _reader;
    readonly ActionRunner _runner;
    readonly FocusSession _focus;
    Guid _workspaceId;
    WorkspaceSnapshot? _snapshot;
    bool _loading;

    public InspectorViewModel(
        WorkspaceReader reader,
        ActionRunner runner,
        FocusSession focus,
        DecisionController decisions
    )
    {
        _reader = reader;
        _runner = runner;
        _focus = focus;
        Decisions = decisions;
    }

    public DecisionController Decisions { get; }
    public ObservableCollection<LifeLine> Life { get; } = [];
    public ObservableCollection<ItemRowViewModel> Subtasks { get; } = [];

    // Links on the item shown, with the status of each one's preview.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasLinks))]
    IReadOnlyList<LinkLine> _links = [];

    public bool HasLinks => Links.Count > 0;

    // Every attribute the domain supports, editable in place. Each change runs a real command, so it
    // lands in the activity log below and stays undoable (docs/07 §10.2).
    public IReadOnlyList<DurationOption> Estimates => FieldOptions.Durations;
    public IReadOnlyList<PriorityOption> Priorities => FieldOptions.Priorities;
    public IReadOnlyList<WhenOption> Whens => FieldOptions.Whens;
    public IReadOnlyList<DueOption> Dues => FieldOptions.Dues;
    public IReadOnlyList<TimeOfDayOption> TimesOfDay => FieldOptions.TimesOfDay;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasItem))]
    TodoItem? _item;

    [ObservableProperty]
    string _titleEdit = "";

    [ObservableProperty]
    DurationOption? _estimate;

    [ObservableProperty]
    PriorityOption? _priority;

    [ObservableProperty]
    WhenOption? _when;

    [ObservableProperty]
    DueOption? _due;

    [ObservableProperty]
    TimeOfDayOption? _timeOfDay;

    [ObservableProperty]
    string _waitingOn = "";

    [ObservableProperty]
    string? _error;

    [ObservableProperty]
    string _notes = "";

    [ObservableProperty]
    string _ageText = "";

    [ObservableProperty]
    string _carryText = "";

    [ObservableProperty]
    string _defersText = "";

    [ObservableProperty]
    string _scheduleText = "";

    [ObservableProperty]
    string _stateText = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasContainerProgress))]
    string? _containerProgress;

    [ObservableProperty]
    bool _showStuckPrompt;

    [ObservableProperty]
    string _stuckPromptText = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFix))]
    string? _fixPrompt;

    public bool HasItem => Item is not null;
    public bool HasContainerProgress => ContainerProgress is not null;
    public bool HasSubtasks => Subtasks.Count > 0;
    public bool HasLife => Life.Count > 0;
    public bool HasFix => FixPrompt is not null;
    public IReadOnlyList<StuckReasonOption> Reasons => ReasonOptions;
    public string Title => Item?.Title ?? "";

    public async Task LoadAsync(Guid? itemId, WorkspaceSnapshot snapshot)
    {
        _snapshot = snapshot;
        _workspaceId = snapshot.Workspace.Id;
        var item = itemId is { } id ? snapshot.Find(id) : null;
        _loading = true;
        Item = item;
        Life.Clear();
        Subtasks.Clear();
        Error = null;
        if (item is null)
        {
            ShowStuckPrompt = false;
            TitleEdit = "";
            Estimate = null;
            Priority = null;
            When = null;
            Due = null;
            TimeOfDay = null;
            WaitingOn = "";
            _loading = false;
            return;
        }

        var metrics = snapshot.MetricsOf(item);
        AgeText = metrics.Age == 0 ? "New" : $"{metrics.Age}d";
        CarryText = metrics.Carry.ToString();
        DefersText = metrics.Defers.ToString();
        Notes = item.Notes ?? "";
        TitleEdit = item.Title;
        Estimate = FieldOptions.DurationFor(item.EstimateMinutes);
        Priority = FieldOptions.PriorityFor(item.Priority);
        When = FieldOptions.WhenFor(item, snapshot.Today);
        Due = FieldOptions.DueFor(item.DueDate, snapshot.Today);
        TimeOfDay = FieldOptions.TimeOfDayFor(item.TimeOfDay);
        WaitingOn = item.WaitingOn ?? "";
        StateText = ItemLabels.StateWord(
            item,
            snapshot.Today,
            snapshot.Workspace.NowItemId == item.Id
        );
        ScheduleText = Schedule(item);
        OnPropertyChanged(nameof(Title));

        var live = item.Status is ItemStatus.Open or ItemStatus.Waiting;
        ShowStuckPrompt =
            live && !item.IsSomeday && StuckPrompt.ShouldPrompt(metrics, snapshot.Thresholds);
        StuckPromptText =
            metrics.Carry > 0
                ? $"This has been carried {metrics.Carry} time{(metrics.Carry == 1 ? "" : "s")}. What's in the way?"
                : $"This has been deferred {metrics.Defers} times. What's in the way?";

        var children = snapshot
            .Items.Where(i => i.ParentId == item.Id && i.DeletedAt is null)
            .ToList();
        foreach (var child in children)
            Subtasks.Add(ItemRowFactory.Create(child, snapshot));
        ContainerProgress =
            item.IsContainer && children.Count > 0
                ? Noto.Core.Insights.ContainerRules.Progress(children).ToString()
                : null;

        var events = await _reader.EventsAsync(item.Id);
        foreach (
            var line in LifeOfItem.Describe(
                item,
                events,
                snapshot.Workspace.DayBoundary,
                snapshot.Today
            )
        )
            Life.Add(line);
        OnPropertyChanged(nameof(HasSubtasks));
        OnPropertyChanged(nameof(HasLife));
        _loading = false;
    }

    // ---- editing ----
    // The setters below fire from the UI only (LoadAsync sets them under _loading), so each one is a
    // deliberate user edit. They run the same commands the rest of the app uses, which means the change
    // shows up in the activity log and ⌘Z undoes it.

    // The title is committed on Enter or when the box loses focus (not per keystroke), like the row editor.
    partial void OnEstimateChanged(DurationOption? value)
    {
        if (value is not null)
            _ = SaveAsync(new SetEstimate(Item!.Id, value.Minutes), "Changed estimate");
    }

    partial void OnPriorityChanged(PriorityOption? value)
    {
        if (value is not null)
            _ = SaveAsync(new SetPriority(Item!.Id, value.Value), $"Priority {value.Value}");
    }

    partial void OnWhenChanged(WhenOption? value)
    {
        if (value is null || _snapshot is not { } snap)
            return;
        ItemCommand command;
        if (value.Someday)
            command = new SetSomeday(Item!.Id, true);
        else if (value.Kind == PlanKind.Unschedule)
            command = new PlanItem(Item!.Id, null, PlanKind.Unschedule);
        else if (value.OffsetDays is { } offset)
            command = new PlanItem(
                Item!.Id,
                snap.Today.AddDays(offset),
                value.Kind ?? PlanKind.Plan
            );
        else
            return;
        _ = SaveAsync(command, $"Planned for {value.Label.ToLowerInvariant()}");
    }

    partial void OnDueChanged(DueOption? value)
    {
        if (value is null || _snapshot is not { } snap)
            return;
        var due = value.InDays is { } days ? snap.Today.AddDays(days) : (DateOnly?)null;
        _ = SaveAsync(
            new SetDueDate(Item!.Id, due),
            due is null ? "Cleared the due date" : "Set a due date"
        );
    }

    partial void OnTimeOfDayChanged(TimeOfDayOption? value)
    {
        if (value is not null)
            _ = SaveAsync(new SetTimeOfDay(Item!.Id, value.Value), "Changed time of day");
    }

    async Task SaveTitleAsync()
    {
        if (_loading || Item is not { } item)
            return;
        var title = TitleEdit.Trim();
        if (title.Length == 0 || title == item.Title)
            return;
        await SaveAsync(new RenameItem(item.Id, title), "Renamed");
    }

    [RelayCommand]
    public async Task CommitTitleAsync() => await SaveTitleAsync();

    [RelayCommand]
    public async Task WaitingChangedAsync()
    {
        if (_loading || Item is not { } item)
            return;
        var on = WaitingOn.Trim();
        if (on.Length == 0)
        {
            if (item.Status == ItemStatus.Waiting)
                await SaveAsync(new EndWaiting(item.Id), "No longer waiting");
            return;
        }
        if (on == item.WaitingOn)
            return;
        await SaveAsync(new StartWaiting(item.Id, on), $"Waiting on {on}");
    }

    // Deleting is a soft delete and stays undoable; the toast offers the undo.
    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (Item is not { } item)
            return;
        await SaveAsync(new DeleteItem(item.Id), $"Deleted “{item.Title}”");
    }

    async Task SaveAsync(ItemCommand command, string label)
    {
        if (_loading || Item is null)
            return;
        try
        {
            Error = null;
            await _runner.RunAsync(command, label);
        }
        // Some transitions are not valid from every state (Someday on a waiting item, say). Say so
        // instead of throwing away a fire-and-forget task, and snap the controls back to reality.
        catch (CommandException e)
        {
            Error = e.Message;
            if (_snapshot is { } snap)
                await LoadAsync(Item.Id, snap);
        }
    }

    string Schedule(TodoItem item)
    {
        var parts = new List<string>();
        if (item.IsSomeday)
            parts.Add("Someday");
        else if (item.PlannedFor is { } p)
            parts.Add(
                $"Planned {p.ToString("ddd MMM d", System.Globalization.CultureInfo.InvariantCulture)}"
            );
        else
            parts.Add("Unscheduled");
        if (item.DueDate is { } due)
            parts.Add(
                $"due {due.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture)}"
            );
        return string.Join(" · ", parts);
    }

    [RelayCommand]
    public async Task SaveNotesAsync()
    {
        if (Item is null || (Item.Notes ?? "") == Notes)
            return;
        await _runner.RunAsync(new SetNotes(Item.Id, Notes), "Edited notes");
    }

    [RelayCommand]
    async Task ChooseOptionAsync(StuckReasonOption option) =>
        await ChooseReasonAsync(option.Reason);

    // Each reason is recorded, then leads to a concrete fix (docs/07 §5).
    public async Task ChooseReasonAsync(StuckReason reason)
    {
        if (Item is not { } item || _snapshot is not { } snap)
            return;
        await _runner.RunAsync(new GiveStuckReason(item.Id, reason), "Recorded why it's stuck");

        var fix = StuckPrompt.FixFor(reason);
        FixPrompt = fix.Prompt;
        var context = new DecisionContext(snap.Today);
        switch (fix.Kind)
        {
            case StuckFixKind.BreakDown:
                await Decisions.BeginAsync(DecisionKind.BreakDown, [item], context);
                break;
            case StuckFixKind.MoveToWaiting:
                await Decisions.BeginAsync(DecisionKind.WaitOn, [item], context);
                break;
            case StuckFixKind.RewriteNextAction:
                await Decisions.BeginAsync(DecisionKind.NextAction, [item], context);
                break;
            case StuckFixKind.Drop:
                await _runner.RunAsync(
                    new DropItem(item.Id, DropReason.NotNeeded),
                    $"Dropped “{item.Title}”"
                );
                break;
            // "Don't want to": the user picks Now (25 min) or tomorrow with the two actions below.
        }
    }

    [RelayCommand]
    public async Task MakeNowAsync()
    {
        if (Item is { } item)
            await _focus.StartAsync(_workspaceId, item);
    }

    [RelayCommand]
    public async Task ScheduleTomorrowAsync()
    {
        if (Item is not { } item || _snapshot is not { } snap)
            return;
        await _runner.RunAsync(
            new PlanItem(item.Id, snap.Today.AddDays(1), PlanKind.Defer),
            "Scheduled for tomorrow"
        );
    }
}
