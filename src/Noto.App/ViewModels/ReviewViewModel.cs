using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Insights;
using Noto.Core.Models;

namespace Noto.App.ViewModels;

public enum ReviewMode { Morning, Shutdown }

public sealed partial class ReviewEntry(ItemRowViewModel row) : ObservableObject
{
    public ItemRowViewModel Row { get; } = row;
    [ObservableProperty] string? _decision;
    public bool IsDecided => Decision is not null;
    partial void OnDecisionChanged(string? value) => OnPropertyChanged(nameof(IsDecided));
}

// One item at a time, one keystroke each (docs/07 §4.1). Shutdown step 2 reuses it with "tomorrow" semantics.
public sealed partial class ReviewViewModel : ObservableObject
{
    readonly AppServices _services;
    readonly Guid _workspaceId;
    readonly List<ReviewEntry> _decided = [];
    ReviewEntry? _pending;

    public ReviewViewModel(AppServices services, Guid workspaceId, ReviewMode mode)
    {
        _services = services;
        _workspaceId = workspaceId;
        Mode = mode;
        Decisions = new DecisionController(services);
        Decisions.Decided += OnDecided;
    }

    public ReviewMode Mode { get; }
    public DecisionController Decisions { get; }
    public ObservableCollection<ReviewEntry> Entries { get; } = [];
    public WorkspaceSnapshot? Snapshot { get; private set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Current), nameof(ProgressText), nameof(CurrentTitle), nameof(CurrentDetail))] int _index;
    [ObservableProperty] string _greeting = "";
    [ObservableProperty] string? _dayNote;
    [ObservableProperty] string? _message;
    [ObservableProperty] CapacityViewModel? _capacity;
    [ObservableProperty] string? _keepAllText;
    [ObservableProperty] bool _isComplete;
    [ObservableProperty] ReviewSuggestion? _suggestion;

    public event Action? Closed;

    public ReviewEntry? Current => Index >= 0 && Index < Entries.Count ? Entries[Index] : null;
    public string ProgressText => Entries.Count == 0 ? "" : $"{Math.Min(Index + 1, Entries.Count)} / {Entries.Count}";
    public bool IsShutdown => Mode == ReviewMode.Shutdown;
    public string CurrentTitle => Current?.Row.Title ?? "";

    public string CurrentDetail => Current is not { } e ? "" :
        $"{(e.Row.Metrics.Age == 0 ? "created today" : $"created {e.Row.Metrics.Age} day{(e.Row.Metrics.Age == 1 ? "" : "s")} ago")}" +
        (e.Row.Metrics.Carry > 0 ? $" · carried ↻{e.Row.Metrics.Carry}" : "") +
        (e.Row.Metrics.Defers > 0 ? $" · deferred {(e.Row.Metrics.Defers == 1 ? "once" : $"{e.Row.Metrics.Defers}×")}" : "");

    public string SuggestionText => Suggestion is { } s ? $"Noto noticed: {s.Text}  [{s.Key}]" : "";
    public bool HasSuggestion => Suggestion is not null;
    public bool HasMessage => Message is not null;
    public bool HasDayNote => DayNote is not null;
    public bool HasKeepAll => KeepAllText is not null;
    public string KeepLabel => IsShutdown ? "Tomorrow" : "Today";
    public string DoneLabel => IsShutdown ? "Done" : "Already done";
    public string KeepAllLabel => IsShutdown ? "Move all to tomorrow" : "Keep all for today";

    DecisionContext Context => new(Snapshot!.Today);

    public async Task LoadAsync()
    {
        Snapshot = await _services.Reader.LoadAsync(_workspaceId);
        var snap = Snapshot;
        var byId = snap.Items.ToDictionary(i => i.Id);

        var items = IsShutdown
            ? snap.Items.Where(i => i.Status == ItemStatus.Open && !i.IsContainer && !i.IsSomeday && i.PlannedFor <= snap.Today)
                .OrderByDescending(i => snap.MetricsOf(i).Carry).ThenByDescending(i => i.Priority).ToList()
            : snap.NeedsDecision;

        Entries.Clear();
        foreach (var item in items) Entries.Add(new ReviewEntry(ItemRowFactory.Create(item, snap, isNow: snap.Workspace.NowItemId == item.Id, byId)));
        Index = 0;
        _decided.Clear();

        if (!IsShutdown)
        {
            var n = Entries.Count;
            Greeting = $"Good morning. {n} item{(n == 1 ? "" : "s")} carried over from yesterday.";
            DayNote = await _services.DayNotes.GetAsync(_workspaceId, snap.Today.AddDays(-1));
        }
        else Greeting = $"{Entries.Count} item{(Entries.Count == 1 ? "" : "s")} not done today.";

        Refresh();
    }

    public async Task<bool> HandleKeyAsync(KeyChord chord)
    {
        if (Decisions.Prompt is not null)
        {
            var handled = await Decisions.HandlePromptKeyAsync(chord);
            await FinishDecisionAsync();
            return handled;
        }
        var binding = KeyMap.Resolve(KeyScope.Review, chord);
        if (binding is null) return false;

        switch (binding.Action)
        {
            case AppAction.PrevReview: Step(-1); return true;
            case AppAction.NextReview: Step(1); return true;
            case AppAction.Escape: Skip(); return true;
        }

        if (Current is not { } entry) return true;
        if (entry.IsDecided) { Message = "Already decided. ⌘Z undoes the last decision."; return true; }
        Message = null;

        var kind = binding.Action switch
        {
            AppAction.KeepToday => IsShutdown ? DecisionKind.KeepTomorrow : DecisionKind.KeepToday,
            AppAction.Defer => DecisionKind.Defer,
            AppAction.Someday => DecisionKind.Someday,
            AppAction.BreakDown => DecisionKind.BreakDown,
            AppAction.WaitOn => DecisionKind.WaitOn,
            AppAction.Drop => DecisionKind.Drop,
            AppAction.AlreadyDone => IsShutdown ? DecisionKind.Complete : DecisionKind.AlreadyDone,
            _ => (DecisionKind?)null,
        };
        if (kind is null) return false;

        _pending = entry;
        await Decisions.BeginAsync(kind.Value, [entry.Row.Item], Context);
        if (Decisions.Prompt is null && Decisions.Message is { } blocked) { Message = blocked; _pending = null; }
        await FinishDecisionAsync();
        return true;
    }

    [RelayCommand]
    public async Task KeepAllAsync()
    {
        foreach (var entry in Entries.Where(e => !e.IsDecided).ToList())
        {
            _pending = entry;
            Index = Entries.IndexOf(entry);
            await Decisions.BeginAsync(IsShutdown ? DecisionKind.KeepTomorrow : DecisionKind.KeepToday, [entry.Row.Item], Context);
            await FinishDecisionAsync();
        }
    }

    // ⌘Z: undo the most recent decision and put its item back in front of the user.
    [RelayCommand]
    async Task UndoLastAsync() => await UndoAsync();

    [RelayCommand]
    async Task PressAsync(string key) => await HandleKeyAsync(new KeyChord(key));

    public async Task<bool> UndoAsync()
    {
        if (_decided.Count == 0) return false;
        var entry = _decided[^1];
        if (await _services.Undo.UndoLastAsync() is null) return false;

        _decided.RemoveAt(_decided.Count - 1);
        entry.Decision = null;
        Index = Entries.IndexOf(entry);
        await RefreshSnapshotAsync();
        Refresh();
        return true;
    }

    // Relentless pressure: the review can't be skipped as a whole (items can still be deferred one by one).
    public bool CanSkip => IsShutdown || Snapshot?.Workspace.Pressure != Pressure.Relentless;

    [RelayCommand]
    public void Skip()
    {
        if (!CanSkip && Entries.Any(e => !e.IsDecided))
        {
            Message = "In this workspace every carried item needs a decision. Defer them one by one.";
            return;
        }
        // Dismissing keeps the banner on Today; the morning review won't reopen until tomorrow.
        if (!IsShutdown) _services.UiState.Set(DismissKey(_workspaceId), Snapshot?.Today.ToString("yyyy-MM-dd"));
        Closed?.Invoke();
    }

    public static string DismissKey(Guid workspaceId) => $"review-dismissed:{workspaceId}";

    void Step(int delta)
    {
        if (Entries.Count == 0) return;
        Index = Math.Clamp(Index + delta, 0, Entries.Count - 1);
        Refresh();
    }

    ReviewEntry? _justDecided;

    void OnDecided(string label)
    {
        if (_pending is not { } entry) return;
        _pending = null;
        entry.Decision = label;
        _decided.Add(entry);
        _justDecided = entry;
    }

    // Runs after the command finished: refresh the live meter, then move to the next undecided item.
    async Task FinishDecisionAsync()
    {
        if (_justDecided is not { } entry) return;
        _justDecided = null;
        await RefreshSnapshotAsync();
        Advance(entry);
    }

    void Advance(ReviewEntry decidedEntry)
    {
        var next = Entries.Skip(Entries.IndexOf(decidedEntry) + 1).FirstOrDefault(e => !e.IsDecided)
                   ?? Entries.FirstOrDefault(e => !e.IsDecided);
        if (next is null) { IsComplete = true; Index = Math.Max(0, Entries.Count - 1); }
        else { IsComplete = false; Index = Entries.IndexOf(next); }
        Refresh();
    }

    async Task RefreshSnapshotAsync() => Snapshot = await _services.Reader.LoadAsync(_workspaceId);

    partial void OnSuggestionChanged(ReviewSuggestion? value) { OnPropertyChanged(nameof(SuggestionText)); OnPropertyChanged(nameof(HasSuggestion)); }
    partial void OnMessageChanged(string? value) => OnPropertyChanged(nameof(HasMessage));
    partial void OnDayNoteChanged(string? value) => OnPropertyChanged(nameof(HasDayNote));
    partial void OnKeepAllTextChanged(string? value) => OnPropertyChanged(nameof(HasKeepAll));

    void Refresh()
    {
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(CurrentDetail));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(ProgressText));
        if (Snapshot is not { } snap) return;

        Suggestion = Current is { IsDecided: false } c ? ReviewSuggestions.For(c.Row.Item, c.Row.Metrics, snap.Thresholds) : null;

        // Live meter: only what's already committed to today; undecided carried items aren't counted yet.
        var view = snap.Today_;
        var committed = view with
        {
            Pinned = [],
            Planned = view.Planned.Where(i => i.PlannedFor == snap.Today).ToList(),
            Now = view.Now?.PlannedFor == snap.Today ? view.Now : null,
        };
        var summary = CapacityBar.Compute(snap.Workspace, committed, snap.MetricsOf, snap.FallbackMinutes);
        Capacity = new CapacityViewModel(summary, usesEstimates: false);

        var pending = Entries.Where(e => !e.IsDecided).Select(e => e.Row.Item).ToList();
        int Cost(TodoItem i) => snap.Workspace.CapacityUnit == CapacityUnit.Items ? 1 : i.EstimateMinutes ?? snap.FallbackMinutes;
        var after = summary.Committed + pending.Sum(Cost);
        var cap = new CapacityViewModel(summary with { Committed = after }, false);
        KeepAllText = IsShutdown || pending.Count == 0 ? null
            : after > summary.Capacity ? $"This puts you at {cap.UsedText} of {cap.TotalText}" : $"Fits: {cap.UsedText} of {cap.TotalText}";
    }
}
