using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Commands;

namespace Noto.App.ViewModels;

// The "+ New task" bar (docs/07 §7.1). Two clear paths:
//   quick    — a title, a duration from the dropdown, Add.
//   detailed — "More options" opens a panel with every field the domain supports.
// Tokens like ~30m or tomorrow still work in the title, but nothing depends on knowing them: the old
// watermark was the only explanation, which is exactly what made the input feel opaque.
public sealed partial class AddItemViewModel(
    AppServices services,
    Guid workspaceId,
    bool plannedForToday,
    Func<string, Guid?>? resolveWorkspace,
    Func<DateOnly> today
) : ObservableObject
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

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

    partial void OnDetailWhenChanged(WhenOption value) =>
        OnPropertyChanged(nameof(DetailWhenPreview));

    partial void OnDetailDueChanged(DueOption value) => OnPropertyChanged(nameof(DetailDuePreview));

    // Backspace at the end of a recognized token removes the whole token.
    public void Backspace() =>
        Text = TokenParser.RemoveTrailingToken(Text, today(), recognizeTags: false);

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

        var target = workspaceId;
        if (parsed.WorkspaceName is { } name)
        {
            if (resolveWorkspace?.Invoke(name) is not { } found)
            {
                Error = $"No workspace called /{name}";
                return false;
            }
            target = found;
        }

        var id = Guid.CreateVersion7();
        var planned = parsed.PlannedFor ?? (plannedForToday ? today() : null);
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

        await services.Runner.RunAllAsync(commands, $"Added “{parsed.Title}”");
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

        var day = today();
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
                workspaceId,
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

        await services.Runner.RunAllAsync(commands, $"Added “{title}”");
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
        IsDetailedOpen = false;
        Dismissed?.Invoke();
    }

    void ResetDetailed()
    {
        DetailTitle = "";
        DetailEstimate = FieldOptions.Durations[0];
        DetailPriority = FieldOptions.Priorities[0];
        DetailWhen = FieldOptions.Whens[0];
        DetailDue = FieldOptions.Dues[0];
        DetailTime = FieldOptions.TimesOfDay[0];
        DetailWaitingOn = "";
        DetailNotes = "";
        DetailError = null;
    }

    ParsedCapture Parse(string input) => TokenParser.Parse(input, today(), recognizeTags: false);

    string Preview(int? offsetDays) =>
        offsetDays is { } days ? today().AddDays(days).ToString("ddd MMM d", Invariant) : "No date";
}
