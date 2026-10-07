using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Models;
using Noto.Core.Presets;

namespace Noto.App.ViewModels;

public enum AppPage { Today, Backlog, Review, Shutdown, Insights, Settings, DayLog, Onboarding, TodayAll, WeeklyReview }

public sealed record WorkspaceRef(Guid Id, string Name);

// What the command bar needs from the shell, kept as an interface so it can be tested alone.
public interface ICommandBarHost
{
    IReadOnlyList<WorkspaceRef> WorkspaceRefs { get; }
    Guid? CurrentWorkspaceId { get; }
    DateOnly Today { get; }
    Task GoAsync(AppPage page);
    Task SelectWorkspaceAsync(Guid id);
    Task OpenItemAsync(Guid workspaceId, Guid itemId);
    Task UndoAsync();
    Task ApplyPresetAsync(Preset preset);
    Task SetPressureAsync(Pressure pressure);
    void ToggleInspector();
    void ToggleSidebar();
    void ShowHelp();
}

public enum ResultKind { Command, Item, Add }

public sealed record CommandResult(ResultKind Kind, string Title, string? Detail, string? Shortcut, Func<Task> Execute)
{
    public string Glyph => Kind switch { ResultKind.Command => "›", ResultKind.Item => "○", _ => "＋" };
}

// ⌘K: ask or jump. Commands, items (full-text search) and "add this" in one ranked list (docs/07 §7.1).
public sealed partial class CommandBarViewModel : ObservableObject
{
    readonly AppServices _services;
    readonly ICommandBarHost _host;

    public CommandBarViewModel(AppServices services, ICommandBarHost host)
    {
        _services = services;
        _host = host;
    }

    public ObservableCollection<CommandResult> Results { get; } = [];

    [ObservableProperty] bool _isOpen;
    [ObservableProperty] string _text = "";
    [ObservableProperty] int _selectedIndex;
    [ObservableProperty] IReadOnlyList<string> _chips = [];

    int _version;

    public async Task OpenAsync(string initial = "")
    {
        IsOpen = true;
        Text = initial;
        await UpdateAsync();
    }

    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        Text = "";
        Results.Clear();
    }

    partial void OnTextChanged(string value) => _ = UpdateAsync();

    public async Task UpdateAsync()
    {
        var version = ++_version;
        var query = Text.Trim();
        var parsed = TokenParser.Parse(query, _host.Today);
        Chips = parsed.Tokens.Select(t => t.Text).ToList();

        var results = new List<CommandResult>();
        results.AddRange(Commands().Select(c => (c, score: FuzzyMatch.Score(query, c.Title))).Where(x => x.score > 0)
            .OrderByDescending(x => x.score).Take(query.Length == 0 ? 40 : 8).Select(x => x.c));

        if (query.Length >= 2)
        {
            var hits = await _services.Search.SearchAsync(query, workspaceId: null, limit: 6);
            if (version != _version) return; // a newer keystroke superseded this lookup
            results.AddRange(hits.Select(h => new CommandResult(ResultKind.Item, h.Title, h.Snippet, null, () => _host.OpenItemAsync(h.WorkspaceId, h.ItemId))));
        }

        if (parsed.Title.Length > 0 && _host.CurrentWorkspaceId is { } wsId)
            results.Add(new CommandResult(ResultKind.Add, $"Add “{parsed.Title}”", Describe(parsed), "↵", () => AddAsync(query, wsId)));

        Results.Clear();
        foreach (var r in results) Results.Add(r);
        SelectedIndex = 0;
    }

    public void Move(int delta)
    {
        if (Results.Count > 0) SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, Results.Count - 1);
    }

    [RelayCommand]
    async Task RunResultAsync(CommandResult result)
    {
        SelectedIndex = Results.IndexOf(result);
        await ExecuteSelectedAsync();
    }

    public async Task ExecuteSelectedAsync()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count) return;
        var result = Results[SelectedIndex];
        Close();
        await result.Execute();
    }

    public async Task<bool> HandleKeyAsync(KeyChord chord)
    {
        switch (chord.Key)
        {
            case "Escape": Close(); return true;
            case "Enter": await ExecuteSelectedAsync(); return true;
            case "ArrowDown": Move(1); return true;
            case "ArrowUp": Move(-1); return true;
            default: return false;
        }
    }

    IEnumerable<CommandResult> Commands()
    {
        CommandResult Go(string title, AppPage page, AppAction? action = null) =>
            new(ResultKind.Command, title, null, action is { } a ? KeyMap.ShortcutFor(a) : null, () => _host.GoAsync(page));

        yield return Go("Go to Today", AppPage.Today, AppAction.JumpToday);
        yield return Go("Go to Backlog", AppPage.Backlog);
        yield return Go("Start review", AppPage.Review, AppAction.StartReview);
        yield return Go("Shut down the day", AppPage.Shutdown, AppAction.Shutdown);
        yield return Go("Go to Insights", AppPage.Insights);
        yield return Go("Go to Settings", AppPage.Settings);
        yield return Go("Today (all workspaces)", AppPage.TodayAll, AppAction.TodayAll);
        yield return Go("Weekly review", AppPage.WeeklyReview);

        var n = 1;
        foreach (var ws in _host.WorkspaceRefs)
        {
            var id = ws.Id;
            yield return new(ResultKind.Command, $"Go to {ws.Name}", null, n <= 9 ? $"⌘{n}" : null, () => _host.SelectWorkspaceAsync(id));
            n++;
        }

        yield return new(ResultKind.Command, "Undo last action", null, KeyMap.ShortcutFor(AppAction.Undo), _host.UndoAsync);
        yield return new(ResultKind.Command, "Toggle inspector", null, KeyMap.ShortcutFor(AppAction.ToggleInspector), () => { _host.ToggleInspector(); return Task.CompletedTask; });
        yield return new(ResultKind.Command, "Toggle sidebar", null, KeyMap.ShortcutFor(AppAction.ToggleSidebar), () => { _host.ToggleSidebar(); return Task.CompletedTask; });
        yield return new(ResultKind.Command, "Show keyboard shortcuts", null, "?", () => { _host.ShowHelp(); return Task.CompletedTask; });

        foreach (var preset in BuiltInPresets.All)
        {
            var p = preset;
            yield return new(ResultKind.Command, $"Switch preset: {p.Name}", $"{p.Layout} · {p.Order} · {p.Pressure}", null, () => _host.ApplyPresetAsync(p));
        }
        foreach (var level in Enum.GetValues<Pressure>())
        {
            var l = level;
            yield return new(ResultKind.Command, $"Set pressure: {l}", null, null, () => _host.SetPressureAsync(l));
        }
    }

    async Task AddAsync(string raw, Guid workspaceId)
    {
        var add = new AddItemViewModel(_services, workspaceId, plannedForToday: true,
            name => _host.WorkspaceRefs.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } w ? w.Id : null,
            () => _host.Today) { Text = raw };
        await add.SubmitAsync();
    }

    static string? Describe(ParsedCapture p)
    {
        var parts = new List<string>();
        if (p.PlannedFor is { } d) parts.Add(d.ToString("ddd MMM d", System.Globalization.CultureInfo.InvariantCulture));
        if (p.EstimateMinutes is { } e) parts.Add($"~{Duration.Short(e)}");
        if (p.Priority > 0) parts.Add($"!{p.Priority}");
        if (p.WaitingOn is { } w) parts.Add($"waiting on {w}");
        if (p.WorkspaceName is { } ws) parts.Add($"in {ws}");
        if (p.Tags.Count > 0) parts.Add(string.Join(' ', p.Tags.Select(t => "#" + t)));
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
