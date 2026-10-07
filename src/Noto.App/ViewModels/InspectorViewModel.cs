using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Commands;
using Noto.Core.Insights;
using Noto.Core.Models;

namespace Noto.App.ViewModels;

public sealed record StuckReasonOption(StuckReason Reason, string Label);

// Details for the focused item: the three numbers, the stuck prompt, subtasks and the life of the item (docs/07 §10.2).
public sealed partial class InspectorViewModel : ObservableObject
{
    public static readonly IReadOnlyList<StuckReasonOption> ReasonOptions =
    [
        new(StuckReason.TooBig, "Too big"), new(StuckReason.Blocked, "Blocked"), new(StuckReason.Unclear, "Unclear what to do"),
        new(StuckReason.DontWant, "Don't want to"), new(StuckReason.NotNeeded, "Not needed"),
    ];

    readonly AppServices _services;
    Guid _workspaceId;
    WorkspaceSnapshot? _snapshot;

    public InspectorViewModel(AppServices services)
    {
        _services = services;
        Decisions = new DecisionController(services);
    }

    public DecisionController Decisions { get; }
    public ObservableCollection<LifeLine> Life { get; } = [];
    public ObservableCollection<ItemRowViewModel> Subtasks { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasItem))] TodoItem? _item;
    [ObservableProperty] string _notes = "";
    [ObservableProperty] string _ageText = "";
    [ObservableProperty] string _carryText = "";
    [ObservableProperty] string _defersText = "";
    [ObservableProperty] string _scheduleText = "";
    [ObservableProperty] string _stateText = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasContainerProgress))] string? _containerProgress;
    [ObservableProperty] bool _showStuckPrompt;
    [ObservableProperty] string _stuckPromptText = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFix))] string? _fixPrompt;

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
        Item = item;
        Life.Clear();
        Subtasks.Clear();
        if (item is null) { ShowStuckPrompt = false; return; }

        var metrics = snapshot.MetricsOf(item);
        AgeText = metrics.Age == 0 ? "New" : $"{metrics.Age}d";
        CarryText = metrics.Carry.ToString();
        DefersText = metrics.Defers.ToString();
        Notes = item.Notes ?? "";
        StateText = ItemLabels.StateWord(item, snapshot.Today, snapshot.Workspace.NowItemId == item.Id);
        ScheduleText = Schedule(item);
        OnPropertyChanged(nameof(Title));

        var live = item.Status is ItemStatus.Open or ItemStatus.Waiting;
        ShowStuckPrompt = live && !item.IsSomeday && StuckPrompt.ShouldPrompt(metrics, snapshot.Thresholds);
        StuckPromptText = metrics.Carry > 0
            ? $"This has been carried {metrics.Carry} time{(metrics.Carry == 1 ? "" : "s")}. What's in the way?"
            : $"This has been deferred {metrics.Defers} times. What's in the way?";

        var children = snapshot.Items.Where(i => i.ParentId == item.Id && i.DeletedAt is null).ToList();
        foreach (var child in children) Subtasks.Add(ItemRowFactory.Create(child, snapshot));
        ContainerProgress = item.IsContainer && children.Count > 0 ? Noto.Core.Insights.ContainerRules.Progress(children).ToString() : null;

        var events = await _services.Reader.EventsAsync(item.Id);
        foreach (var line in LifeOfItem.Describe(item, events, snapshot.Workspace.DayBoundary, snapshot.Today)) Life.Add(line);
        OnPropertyChanged(nameof(HasSubtasks));
        OnPropertyChanged(nameof(HasLife));
    }

    string Schedule(TodoItem item)
    {
        var parts = new List<string>();
        if (item.IsSomeday) parts.Add("Someday");
        else if (item.PlannedFor is { } p) parts.Add($"Planned {p.ToString("ddd MMM d", System.Globalization.CultureInfo.InvariantCulture)}");
        else parts.Add("Unscheduled");
        if (item.DueDate is { } due) parts.Add($"due {due.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture)}");
        return string.Join(" · ", parts);
    }

    [RelayCommand]
    public async Task SaveNotesAsync()
    {
        if (Item is null || (Item.Notes ?? "") == Notes) return;
        await _services.Runner.RunAsync(new SetNotes(Item.Id, Notes), "Edited notes");
    }

    [RelayCommand]
    async Task ChooseOptionAsync(StuckReasonOption option) => await ChooseReasonAsync(option.Reason);

    // Each reason is recorded, then leads to a concrete fix (docs/07 §5).
    public async Task ChooseReasonAsync(StuckReason reason)
    {
        if (Item is not { } item || _snapshot is not { } snap) return;
        await _services.Runner.RunAsync(new GiveStuckReason(item.Id, reason), "Recorded why it's stuck");

        var fix = StuckPrompt.FixFor(reason);
        FixPrompt = fix.Prompt;
        var context = new DecisionContext(snap.Today);
        switch (fix.Kind)
        {
            case StuckFixKind.BreakDown: await Decisions.BeginAsync(DecisionKind.BreakDown, [item], context); break;
            case StuckFixKind.MoveToWaiting: await Decisions.BeginAsync(DecisionKind.WaitOn, [item], context); break;
            case StuckFixKind.RewriteNextAction: await Decisions.BeginAsync(DecisionKind.NextAction, [item], context); break;
            case StuckFixKind.Drop:
                await _services.Runner.RunAsync(new DropItem(item.Id, DropReason.NotNeeded), $"Dropped “{item.Title}”");
                break;
            // "Don't want to": the user picks Now (25 min) or tomorrow with the two actions below.
        }
    }

    [RelayCommand]
    public async Task MakeNowAsync()
    {
        if (Item is { } item) await _services.Focus.StartAsync(_workspaceId, item);
    }

    [RelayCommand]
    public async Task ScheduleTomorrowAsync()
    {
        if (Item is not { } item || _snapshot is not { } snap) return;
        await _services.Runner.RunAsync(new PlanItem(item.Id, snap.Today.AddDays(1), PlanKind.Defer), "Scheduled for tomorrow");
    }
}
