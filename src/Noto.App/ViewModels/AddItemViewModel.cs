using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Ai;
using Noto.Core.Commands;
using Noto.Core.Links;
using Noto.Core.Text;

namespace Noto.App.ViewModels;

// The "+ New task" bar (docs/07 §7.1). Two clear paths:
//   quick    — a title, a duration from the dropdown, Add.
//   detailed — "More options" opens a panel with every field the domain supports.
// Tokens like ~30m or tomorrow still work in the title, but nothing depends on knowing them: the old
// watermark was the only explanation, which is exactly what made the input feel opaque.
//
// The detailed panel also carries the optional fill: paste a rough title, ask the model for the rest.
// It writes into the form's own fields — never into a task — so the user reviews it, and can undo the
// whole fill in one step (docs/07 §19: AI suggests, the user decides).
public sealed partial class AddItemViewModel : ObservableObject
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    readonly ActionRunner _runner;
    readonly Guid _workspaceId;
    readonly bool _plannedForToday;
    readonly Func<string, Guid?>? _resolveWorkspace;
    readonly Func<DateOnly> _today;
    readonly ISuggestionService? _suggestions;
    readonly ILinkResolver? _links;

    // Set while the fill is writing the panel, so a title the fill itself produced does not invalidate
    // the fill it came from.
    bool _applyingFill;

    // What the panel held before the fill, so Undo can put it back exactly.
    FillSnapshot? _beforeFill;

    public AddItemViewModel(
        ActionRunner runner,
        Guid workspaceId,
        bool plannedForToday,
        Func<string, Guid?>? resolveWorkspace,
        Func<DateOnly> today,
        ISuggestionService? suggestions = null,
        ILinkResolver? links = null
    )
    {
        _runner = runner;
        _workspaceId = workspaceId;
        _plannedForToday = plannedForToday;
        _resolveWorkspace = resolveWorkspace;
        _today = today;
        _suggestions = suggestions;
        _links = links;

        // Switching AI off in Settings must remove every affordance from an already-open panel.
        if (suggestions is INotifyPropertyChanged notifier)
            notifier.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is null or nameof(ISuggestionService.IsEnabled))
                {
                    OnPropertyChanged(nameof(HasSuggestions));
                    OnPropertyChanged(nameof(ShowFillAction));
                    OnPropertyChanged(nameof(CanSuggest));
                }
            };
    }

    // ---- quick create ----

    [ObservableProperty]
    string _text = "";

    [ObservableProperty]
    string? _error;

    [ObservableProperty]
    IReadOnlyList<string> _chips = [];

    [ObservableProperty]
    DurationOption _estimate = FieldOptions.Durations[0];

    // ---- detailed create ----

    [ObservableProperty]
    bool _isDetailedOpen;

    [ObservableProperty]
    string _detailTitle = "";

    [ObservableProperty]
    DurationOption _detailEstimate = FieldOptions.Durations[0];

    [ObservableProperty]
    PriorityOption _detailPriority = FieldOptions.Priorities[0];

    [ObservableProperty]
    WhenOption _detailWhen = FieldOptions.Whens[0];

    [ObservableProperty]
    DueOption _detailDue = FieldOptions.Dues[0];

    [ObservableProperty]
    TimeOfDayOption _detailTime = FieldOptions.TimesOfDay[0];

    [ObservableProperty]
    string _detailWaitingOn = "";

    [ObservableProperty]
    string _detailNotes = "";

    [ObservableProperty]
    string? _detailError;

    // ---- the fill ----
    //
    // The panel is in exactly one of four states: idle (the action), working, filled, or failed. One
    // strip under the title carries all four, so there is never a second place to look.

    // True only when a provider is configured and switched on; everything else disappears otherwise.
    public bool HasSuggestions => _suggestions is { IsEnabled: true };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFillAction), nameof(CanSuggest))]
    bool _isSuggesting;

    // True once a fill has been applied and not yet undone — the state that shows the evidence.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFillAction), nameof(CanSuggest))]
    bool _hasFill;

    // What it is doing right now: "Reading github.com…", "Filling the fields…".
    [ObservableProperty]
    string? _suggestionStep;

    [ObservableProperty]
    string? _suggestionError;

    // Why the model filled what it did — the evidence the user reviews before saving.
    [ObservableProperty]
    string? _suggestionNote;

    // Which fields were written, named, so nothing changes without the user being told.
    [ObservableProperty]
    string? _filledFields;

    // The action, offered whenever nothing is in flight and there is nothing to review.
    public bool ShowFillAction => HasSuggestions && !IsSuggesting && !HasFill;

    // The same, but only with a title to work from — otherwise the action could do nothing.
    public bool CanSuggest => ShowFillAction && DetailTitle.Trim().Length > 0;

    public IReadOnlyList<DurationOption> Estimates => FieldOptions.Durations;
    public IReadOnlyList<PriorityOption> Priorities => FieldOptions.Priorities;
    public IReadOnlyList<WhenOption> Whens => FieldOptions.Whens;
    public IReadOnlyList<DueOption> Dues => FieldOptions.Dues;
    public IReadOnlyList<TimeOfDayOption> TimesOfDay => FieldOptions.TimesOfDay;

    // The panel resolves relative choices to real dates so "In a week" is unambiguous before saving.
    public string DetailWhenPreview => Preview(DetailWhen.OffsetDays);
    public string DetailDuePreview => Preview(DetailDue.InDays);

    public event Action? Added;

    // Raised when the bar is dismissed (Escape, or a click outside it) so the view can drop focus.
    public event Action? Dismissed;

    public Guid? LastCreatedId { get; private set; }

    partial void OnTextChanged(string value)
    {
        Error = null;
        Chips = Parse(value).Tokens.Select(t => t.Text).ToList();
    }

    // Editing the title moves the panel on, so the last fill's evidence no longer describes what is on
    // screen. It goes away — including its undo — rather than offering to revert a title the user has
    // since rewritten.
    partial void OnDetailTitleChanged(string value)
    {
        if (!_applyingFill)
            ClearFill();
        OnPropertyChanged(nameof(CanSuggest));
    }

    partial void OnDetailWhenChanged(WhenOption value) =>
        OnPropertyChanged(nameof(DetailWhenPreview));

    partial void OnDetailDueChanged(DueOption value) => OnPropertyChanged(nameof(DetailDuePreview));

    // Backspace at the end of a recognized token removes the whole token.
    public void Backspace() =>
        Text = TokenParser.RemoveTrailingToken(Text, _today(), recognizeTags: false);

    // Fill the panel from the raw title. A link in the text is resolved first, so the model is handed
    // what the page *is* rather than left to guess at a bare URL.
    [RelayCommand]
    public async Task SuggestAsync()
    {
        if (_suggestions is not { IsEnabled: true })
            return;

        var raw = DetailTitle.Trim();
        if (raw.Length == 0)
        {
            SuggestionError = "Type or paste a title first.";
            return;
        }

        ClearFill();
        IsSuggesting = true;
        try
        {
            var url = LinkUrl.Scan(raw).FirstOrDefault();
            SuggestionStep = url is null ? "Filling the fields…" : $"Reading {HostOf(url)}…";
            var link = await ResolveLinkAsync(url);

            SuggestionStep = "Filling the fields…";
            var draft = await _suggestions.SuggestAsync(new SuggestionRequest(raw, _today(), link));
            if (draft is null || draft.IsEmpty)
            {
                SuggestionError = "Nothing to add from that.";
                return;
            }

            var filled = Apply(draft);
            SuggestionNote = Note(draft);
            FilledFields = filled.Count > 0 ? string.Join(", ", filled) : null;
            HasFill = true;
        }
        catch (OperationCanceledException)
        {
            // The panel closed or the user retyped; nothing to report.
        }
        catch (AiSuggestionException e)
        {
            SuggestionError = e.Message;
        }
        finally
        {
            IsSuggesting = false;
            SuggestionStep = null;
        }
    }

    // Resolution never blocks the fill, and it never pretends a link was absent: a URL that could not
    // be read is handed over as exactly that, so the model is told not to invent its contents.
    async Task<LinkContext?> ResolveLinkAsync(string? url)
    {
        if (url is null)
            return null;
        if (_links is null)
            return new LinkContext(url, null, null, null, null, [], "link reading is unavailable");

        try
        {
            return await _links.ResolveAsync(url);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new LinkContext(url, null, null, null, null, [], "couldn't be read");
        }
    }

    static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    // Only overwrite what the model actually filled, so a partial suggestion never wipes an edit.
    // Returns the human names of the fields that changed, for the evidence line.
    List<string> Apply(FieldSuggestion draft)
    {
        _beforeFill = new FillSnapshot(
            DetailTitle,
            DetailEstimate,
            DetailPriority,
            DetailWhen,
            DetailDue,
            DetailTime,
            DetailWaitingOn,
            DetailNotes
        );

        _applyingFill = true;
        var filled = new List<string>();
        try
        {
            if (draft.Title is { } title)
            {
                DetailTitle = title;
                filled.Add("title");
            }
            if (draft.EstimateMinutes is { } minutes)
            {
                DetailEstimate = FieldOptions.DurationFor(minutes);
                filled.Add("estimate");
            }
            if (draft.Priority is { } priority)
            {
                DetailPriority = FieldOptions.PriorityFor(priority);
                filled.Add("priority");
            }
            if (draft.PlannedFor is { } planned)
            {
                DetailWhen = MapWhen(planned, _today());
                filled.Add("when");
            }
            if (draft.DueDate is { } due)
            {
                DetailDue = FieldOptions.DueFor(due, _today());
                filled.Add("due date");
            }
            if (draft.TimeOfDay is { } slot)
            {
                DetailTime = FieldOptions.TimeOfDayFor(slot);
                filled.Add("time of day");
            }
            if (draft.WaitingOn is { } waiting)
            {
                DetailWaitingOn = waiting;
                filled.Add("waiting on");
            }
            if (draft.Notes is { } notes)
            {
                DetailNotes = notes;
                filled.Add("notes");
            }
        }
        finally
        {
            _applyingFill = false;
        }
        return filled;
    }

    // One step back to exactly what the panel held, so a bad fill costs a keystroke, not a cleanup.
    [RelayCommand]
    public void UndoFill()
    {
        if (_beforeFill is not { } before)
            return;

        _applyingFill = true;
        try
        {
            DetailTitle = before.Title;
            DetailEstimate = before.Estimate;
            DetailPriority = before.Priority;
            DetailWhen = before.When;
            DetailDue = before.Due;
            DetailTime = before.Time;
            DetailWaitingOn = before.WaitingOn;
            DetailNotes = before.Notes;
        }
        finally
        {
            _applyingFill = false;
        }
        ClearFill();
    }

    // Everything the panel remembers about the last fill. Called before a new one and on undo, dismiss
    // and submit, so a stale evidence strip can never outlive the values it describes.
    void ClearFill()
    {
        _beforeFill = null;
        HasFill = false;
        SuggestionError = null;
        SuggestionNote = null;
        FilledFields = null;
        SuggestionStep = null;
    }

    static string Note(FieldSuggestion draft)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(draft.Rationale))
            parts.Add(draft.Rationale.Trim());
        // Tags have no field in the form yet, so they are shown rather than dropped silently.
        if (draft.Tags.Count > 0)
            parts.Add("tags: " + string.Join(", ", draft.Tags));
        return string.Join(" · ", parts);
    }

    // The form offers presets, not arbitrary dates, so a concrete day maps to the nearest one.
    static WhenOption MapWhen(DateOnly planned, DateOnly today)
    {
        var offset = planned.DayNumber - today.DayNumber;
        return offset <= 0 ? FieldOptions.Whens[0]
            : offset == 1 ? FieldOptions.Whens[1]
            : FieldOptions.Whens[2];
    }

    // What the panel held before a fill, so Undo can put it back exactly.
    sealed record FillSnapshot(
        string Title,
        DurationOption Estimate,
        PriorityOption Priority,
        WhenOption When,
        DueOption Due,
        TimeOfDayOption Time,
        string WaitingOn,
        string Notes
    );

    [RelayCommand]
    async Task AddAsync() => await SubmitAsync();

    // Quick create: the dropdown duration, plus any tokens typed into the title.
    public async Task<bool> SubmitAsync()
    {
        var parsed = Parse(Text);
        if (parsed.Title.Length == 0)
        {
            Error = "Give the task a name";
            return false;
        }

        var target = _workspaceId;
        if (parsed.WorkspaceName is { } name)
        {
            if (_resolveWorkspace?.Invoke(name) is not { } found)
            {
                Error = $"No workspace called /{name}";
                return false;
            }
            target = found;
        }

        var id = Guid.CreateVersion7();
        var planned = parsed.PlannedFor ?? (_plannedForToday ? _today() : null);
        var commands = new List<ItemCommand>
        {
            new CreateItem(
                id,
                target,
                parsed.Title,
                planned,
                false,
                Estimate.Minutes ?? parsed.EstimateMinutes
            ),
        };
        if (parsed.Priority > 0)
            commands.Add(new SetPriority(id, parsed.Priority));
        if (parsed.WaitingOn is { } on)
            commands.Add(new StartWaiting(id, on));

        await _runner.RunAllAsync(commands, $"Added “{parsed.Title}”");
        LastCreatedId = id;
        Text = "";
        Estimate = FieldOptions.Durations[0];
        Added?.Invoke();
        return true;
    }

    [RelayCommand]
    void OpenDetailed()
    {
        // Carry over what has been typed so far; nothing is lost by looking at the other fields.
        DetailTitle = Text;
        DetailError = null;
        ClearFill();
        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(ShowFillAction));
        OnPropertyChanged(nameof(CanSuggest));
        IsDetailedOpen = true;
    }

    [RelayCommand]
    void CloseDetailed() => Dismiss(keepTitle: true);

    [RelayCommand]
    async Task DetailSubmitAsync() => await SubmitDetailedAsync();

    // Detailed create: every field is explicit, so the title is taken literally.
    public async Task<bool> SubmitDetailedAsync()
    {
        var title = DetailTitle.Trim();
        if (title.Length == 0)
        {
            DetailError = "Give the task a name";
            return false;
        }

        var day = _today();
        var id = Guid.CreateVersion7();
        DateOnly? planned = null;
        if (
            !DetailWhen.Someday
            && DetailWhen.Kind != PlanKind.Unschedule
            && DetailWhen.OffsetDays is { } offset
        )
            planned = day.AddDays(offset);

        var commands = new List<ItemCommand>
        {
            new CreateItem(
                id,
                _workspaceId,
                title,
                planned,
                DetailWhen.Someday,
                DetailEstimate.Minutes
            ),
        };
        if (DetailPriority.Value > 0)
            commands.Add(new SetPriority(id, DetailPriority.Value));
        if (DetailDue.InDays is { } due)
            commands.Add(new SetDueDate(id, day.AddDays(due)));
        if (DetailTime.Value is { } slot)
            commands.Add(new SetTimeOfDay(id, slot));
        if (DetailWaitingOn.Trim() is { Length: > 0 } on)
            commands.Add(new StartWaiting(id, on));
        if (DetailNotes.Trim() is { Length: > 0 } notes)
            commands.Add(new SetNotes(id, notes));

        await _runner.RunAllAsync(commands, $"Added “{title}”");
        LastCreatedId = id;
        ResetDetailed();
        IsDetailedOpen = false;
        Added?.Invoke();
        return true;
    }

    // Clicking outside the bar (or Escape) leaves it without losing what was typed.
    public void Dismiss(bool keepTitle = false)
    {
        if (keepTitle && Text.Trim().Length == 0 && DetailTitle.Trim().Length > 0)
            Text = DetailTitle;
        Error = null;
        DetailError = null;
        ClearFill();
        IsDetailedOpen = false;
        Dismissed?.Invoke();
    }

    void ResetDetailed()
    {
        _applyingFill = true;
        try
        {
            DetailTitle = "";
            DetailEstimate = FieldOptions.Durations[0];
            DetailPriority = FieldOptions.Priorities[0];
            DetailWhen = FieldOptions.Whens[0];
            DetailDue = FieldOptions.Dues[0];
            DetailTime = FieldOptions.TimesOfDay[0];
            DetailWaitingOn = "";
            DetailNotes = "";
        }
        finally
        {
            _applyingFill = false;
        }
        DetailError = null;
        ClearFill();
    }

    ParsedCapture Parse(string input) => TokenParser.Parse(input, _today(), recognizeTags: false);

    string Preview(int? offsetDays) =>
        offsetDays is { } days
            ? _today().AddDays(days).ToString("ddd MMM d", Invariant)
            : "No date";
}
