using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Commands;
using Noto.Core.Models;

namespace Noto.App.ViewModels;

public enum DecisionKind { KeepToday, KeepTomorrow, Defer, Someday, BreakDown, WaitOn, Drop, AlreadyDone, Complete, NextAction, SetEstimate }

public enum PromptKind { DeferDate, WaitingOn, BreakDown, DropReason, NextAction, Estimate }

public sealed record DecisionContext(DateOnly Today);

public sealed record DropReasonOption(string Digit, string Label);

public sealed partial class DecisionPrompt : ObservableObject
{
    public required PromptKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Hint { get; init; }
    public required IReadOnlyList<TodoItem> Targets { get; init; }

    [ObservableProperty] string _text = "";
    [ObservableProperty] string? _error;

    public bool IsTextual => Kind != PromptKind.DropReason;
    public bool IsDropReason => Kind == PromptKind.DropReason;
}

// Turns a decision (Today, Defer, Someday, …) into commands, asking for a date / person / subtasks / reason first when needed.
// Shared by lists, the morning review and shutdown so the keys behave the same everywhere.
public sealed partial class DecisionController(AppServices services) : ObservableObject
{
    static readonly (DropReason Reason, string Label)[] DropReasons =
    [
        (DropReason.NotNeeded, "1 No longer needed"),
        (DropReason.SomeoneElseDidIt, "2 Someone else did it"),
        (DropReason.NotWorthIt, "3 Not worth it"),
    ];

    [ObservableProperty] DecisionPrompt? _prompt;
    [ObservableProperty] string? _message;

    DecisionContext _context = new(default);

    public event Action<string>? Decided;

    public IReadOnlyList<DropReasonOption> DropReasonOptions { get; } =
        DropReasons.Select((r, n) => new DropReasonOption((n + 1).ToString(), r.Label)).ToList();

    public async Task BeginAsync(DecisionKind kind, IReadOnlyList<TodoItem> targets, DecisionContext context)
    {
        Message = null;
        _context = context;
        if (targets.Count == 0) return;

        switch (kind)
        {
            case DecisionKind.Defer:
                Prompt = NewPrompt(PromptKind.DeferDate, "Defer to…", "fri, next week, +3, oct 12", targets);
                return;
            case DecisionKind.WaitOn:
                Prompt = NewPrompt(PromptKind.WaitingOn, "Waiting on whom or what?", "a person or a link", targets);
                return;
            case DecisionKind.BreakDown:
                if (targets.Count != 1) { Message = "Break down works on one item at a time"; return; }
                Prompt = NewPrompt(PromptKind.BreakDown, "Break into 2–5 steps", "separate steps with ;", targets);
                return;
            case DecisionKind.NextAction:
                Prompt = NewPrompt(PromptKind.NextAction, "What's the very next physical action?", "the title is rewritten with your answer", targets);
                return;
            case DecisionKind.SetEstimate:
                Prompt = NewPrompt(PromptKind.Estimate, "Estimate", "15m, 2h, 1h30m, or s / m / l", targets);
                return;
            case DecisionKind.Drop:
                Prompt = NewPrompt(PromptKind.DropReason, "Drop — why?", "1–3 to pick, Enter for “No longer needed”", targets);
                return;
        }
        await ApplyImmediateAsync(kind, targets);
    }

    [RelayCommand]
    public void Cancel() { Prompt = null; }

    [RelayCommand]
    async Task ChooseDropReasonAsync(string digit) => await HandlePromptKeyAsync(new KeyChord(digit));

    // Prompt keys: Enter submits, Escape cancels, and drop reasons are picked with 1–3.
    public async Task<bool> HandlePromptKeyAsync(KeyChord chord)
    {
        if (Prompt is null) return false;
        if (chord.Key == "Escape") { Cancel(); return true; }
        if (chord.Key == "Enter") { await SubmitAsync(); return true; }
        if (Prompt.Kind == PromptKind.DropReason)
        {
            if (chord.Key is "1" or "2" or "3") await SubmitDropAsync(DropReasons[chord.Key[0] - '1'].Reason);
            else if (chord.Key is "x" or "Backspace") await SubmitDropAsync(DropReason.NotNeeded);
            return true;
        }
        return false; // typing goes to the text box
    }

    [RelayCommand]
    public async Task SubmitAsync()
    {
        if (Prompt is not { } prompt) return;
        var text = prompt.Text.Trim();
        var targets = prompt.Targets;
        prompt.Error = null;

        switch (prompt.Kind)
        {
            case PromptKind.DeferDate:
                if (!NaturalDate.TryParse(text, _context.Today, out var date) || date <= _context.Today)
                {
                    prompt.Error = "Pick a future day, like “fri” or “+3”";
                    return;
                }
                await RunAsync(targets.Select(t => (ItemCommand)new PlanItem(t.Id, date, PlanKind.Defer)).ToList(),
                    $"Deferred {Noun(targets)} to {date.ToString("ddd MMM d", System.Globalization.CultureInfo.InvariantCulture)}");
                break;

            case PromptKind.WaitingOn:
                if (text.Length == 0) { prompt.Error = "Say who or what you're waiting on"; return; }
                await RunAsync(targets.Select(t => (ItemCommand)new StartWaiting(t.Id, text)).ToList(), $"Waiting on {text}");
                break;

            case PromptKind.BreakDown:
                var steps = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (steps.Length is < 2 or > 5) { prompt.Error = "Add 2–5 steps separated by ;"; return; }
                await RunAsync([new BreakDown(targets[0].Id, steps)], $"Broke down “{targets[0].Title}”");
                break;

            case PromptKind.NextAction:
                if (text.Length == 0) { prompt.Error = "Write the next action"; return; }
                await RunAsync([new RenameItem(targets[0].Id, text)], "Rewrote title");
                break;

            case PromptKind.Estimate:
                var minutes = TokenParser.Parse("~" + text.TrimStart('~'), _context.Today).EstimateMinutes;
                if (minutes is null) { prompt.Error = "Try 30m, 2h or s / m / l"; return; }
                await RunAsync(targets.Select(t => (ItemCommand)new SetEstimate(t.Id, minutes)).ToList(), $"Estimated {Noun(targets)} at {Duration.Short(minutes.Value)}");
                break;

            case PromptKind.DropReason:
                await SubmitDropAsync(DropReason.NotNeeded);
                return;
        }
    }

    async Task SubmitDropAsync(DropReason reason)
    {
        var targets = Prompt!.Targets;
        await RunAsync(targets.Select(t => (ItemCommand)new DropItem(t.Id, reason)).ToList(), $"Dropped {Noun(targets)}");
    }

    async Task ApplyImmediateAsync(DecisionKind kind, IReadOnlyList<TodoItem> targets)
    {
        var today = _context.Today;
        var tomorrow = today.AddDays(1);

        var (commands, label) = kind switch
        {
            DecisionKind.KeepToday => (Pick(targets, t => t.PlannedFor != today || t.IsSomeday)
                .Select(t => (ItemCommand)new PlanItem(t.Id, null, PlanKind.KeepToday)).ToList(), $"Kept {Noun(targets)} for today"),
            // Evening shutdown: moving to tomorrow is an explicit defer, so it still counts toward "stuck".
            DecisionKind.KeepTomorrow => (targets.Select(t => (ItemCommand)new PlanItem(t.Id, tomorrow, PlanKind.Defer)).ToList(), $"Moved {Noun(targets)} to tomorrow"),
            DecisionKind.Someday => (Pick(targets, t => t.Status == ItemStatus.Open && !t.IsSomeday)
                .Select(t => (ItemCommand)new SetSomeday(t.Id, true)).ToList(), $"Moved {Noun(targets)} to Someday"),
            DecisionKind.AlreadyDone => (Pick(targets, t => t.Status is ItemStatus.Open or ItemStatus.Waiting)
                .Select(t => (ItemCommand)new CompleteItem(t.Id, today.AddDays(-1))).ToList(), $"Marked {Noun(targets)} done yesterday"),
            DecisionKind.Complete => (targets.Select(t => t.Status == ItemStatus.Done
                ? (ItemCommand)new ReopenItem(t.Id) : new CompleteItem(t.Id)).ToList(), $"Completed {Noun(targets)}"),
            _ => ([], ""),
        };

        if (commands.Count == 0) { Message = "Nothing to change"; return; }
        await RunAsync(commands, label);
    }

    async Task RunAsync(IReadOnlyList<ItemCommand> commands, string label)
    {
        Prompt = null;
        try
        {
            await services.Runner.RunAllAsync(commands, label);
            Decided?.Invoke(label);
        }
        catch (CommandException e) { Message = e.Message; }
        catch (InvariantViolationException e) { Message = e.Message; }
    }

    DecisionPrompt NewPrompt(PromptKind kind, string title, string hint, IReadOnlyList<TodoItem> targets) =>
        new() { Kind = kind, Title = title, Hint = hint, Targets = targets };

    static IEnumerable<TodoItem> Pick(IReadOnlyList<TodoItem> items, Func<TodoItem, bool> predicate) => items.Where(predicate);

    static string Noun(IReadOnlyList<TodoItem> items) => items.Count == 1 ? $"“{items[0].Title}”" : $"{items.Count} items";
}
