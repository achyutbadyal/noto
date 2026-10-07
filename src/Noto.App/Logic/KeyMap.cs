namespace Noto.App.Logic;

// A normalized key press. Printable keys are their lowercase character; named keys use Avalonia's names.
public readonly record struct KeyChord(string Key, bool Command = false, bool Shift = false, bool Alt = false)
{
    public override string ToString() =>
        (Command ? "cmd+" : "") + (Shift ? "shift+" : "") + (Alt ? "alt+" : "") + Key;

    public static KeyChord Of(string spec)
    {
        var parts = spec.Split('+');
        return new(parts[^1], parts.Contains("cmd"), parts.Contains("shift"), parts.Contains("alt"));
    }
}

public enum AppAction
{
    CommandBar, ToggleInspector, ToggleSidebar, JumpToday, WorkspaceN, TodayAll, ModeSwitcher, StartReview, Shutdown, Undo,
    MoveDown, MoveUp, ExtendDown, ExtendUp, SelectAll, Complete, Edit, MakeNow, Defer, KeepToday, Someday, WaitOn, BreakDown,
    Drop, SetPriority, SetEstimate, NewItem, Search, PrevDay, NextDay, Help, GoBacklog, GoToday, AlreadyDone,
    Escape, Confirm, PrevReview, NextReview,
}

public enum KeyScope { Global, List, Review }

public sealed record Binding(KeyScope Scope, string Chord, AppAction Action, string Display, string Description, int Arg = 0);

public static class KeyMap
{
    public static readonly IReadOnlyList<Binding> All = Build();

    // `Display` uses ⌘ glyphs for macOS; Windows/Linux show Ctrl in their own presentation layer.
    static List<Binding> Build()
    {
        var b = new List<Binding>
        {
            new(KeyScope.Global, "cmd+k", AppAction.CommandBar, "⌘K", "Command bar"),
            new(KeyScope.Global, "cmd+i", AppAction.ToggleInspector, "⌘I", "Toggle inspector"),
            new(KeyScope.Global, "cmd+b", AppAction.ToggleSidebar, "⌘B", "Toggle sidebar"),
            new(KeyScope.Global, "cmd+t", AppAction.JumpToday, "⌘T", "Jump to today"),
            new(KeyScope.Global, "cmd+0", AppAction.TodayAll, "⌘0", "Today (all workspaces)"),
            new(KeyScope.Global, "cmd+shift+m", AppAction.ModeSwitcher, "⌘⇧M", "Switch mode / preset"),
            new(KeyScope.Global, "cmd+shift+r", AppAction.StartReview, "⌘⇧R", "Start review"),
            new(KeyScope.Global, "cmd+shift+d", AppAction.Shutdown, "⌘⇧D", "Shutdown"),
            new(KeyScope.Global, "cmd+z", AppAction.Undo, "⌘Z", "Undo"),
        };
        for (var n = 1; n <= 9; n++) b.Add(new(KeyScope.Global, $"cmd+{n}", AppAction.WorkspaceN, $"⌘{n}", $"Workspace {n}", n));

        b.AddRange(
        [
            new(KeyScope.List, "j", AppAction.MoveDown, "j / ↓", "Move down"),
            new(KeyScope.List, "ArrowDown", AppAction.MoveDown, "↓", "Move down"),
            new(KeyScope.List, "k", AppAction.MoveUp, "k / ↑", "Move up"),
            new(KeyScope.List, "ArrowUp", AppAction.MoveUp, "↑", "Move up"),
            new(KeyScope.List, "shift+j", AppAction.ExtendDown, "⇧J", "Extend selection down"),
            new(KeyScope.List, "shift+k", AppAction.ExtendUp, "⇧K", "Extend selection up"),
            new(KeyScope.List, "cmd+a", AppAction.SelectAll, "⌘A", "Select all"),
            new(KeyScope.List, "x", AppAction.Complete, "x", "Complete"),
            new(KeyScope.List, "e", AppAction.Edit, "e", "Edit title"),
            new(KeyScope.List, "Enter", AppAction.Edit, "Enter", "Edit title"),
            new(KeyScope.List, "f", AppAction.MakeNow, "f", "Make Now (focus)"),
            new(KeyScope.List, "d", AppAction.Defer, "d", "Defer…"),
            new(KeyScope.List, "t", AppAction.KeepToday, "t", "Plan for today"),
            new(KeyScope.List, "s", AppAction.Someday, "s", "Someday"),
            new(KeyScope.List, "w", AppAction.WaitOn, "w", "Waiting on…"),
            new(KeyScope.List, "b", AppAction.BreakDown, "b", "Break down"),
            new(KeyScope.List, "Backspace", AppAction.Drop, "⌫", "Drop"),
            new(KeyScope.List, "~", AppAction.SetEstimate, "~", "Set estimate"),
            new(KeyScope.List, "n", AppAction.NewItem, "n", "New item"),
            new(KeyScope.List, "/", AppAction.Search, "/", "Search"),
            new(KeyScope.List, "[", AppAction.PrevDay, "[", "Previous day"),
            new(KeyScope.List, "]", AppAction.NextDay, "]", "Next day"),
            new(KeyScope.List, "?", AppAction.Help, "?", "Shortcut help"),

            new(KeyScope.Review, "t", AppAction.KeepToday, "T", "Today"),
            new(KeyScope.Review, "d", AppAction.Defer, "D", "Defer…"),
            new(KeyScope.Review, "s", AppAction.Someday, "S", "Someday"),
            new(KeyScope.Review, "b", AppAction.BreakDown, "B", "Break down"),
            new(KeyScope.Review, "w", AppAction.WaitOn, "W", "Waiting on…"),
            new(KeyScope.Review, "x", AppAction.Drop, "X", "Drop"),
            new(KeyScope.Review, "a", AppAction.AlreadyDone, "A", "Already done"),
            new(KeyScope.Review, "ArrowLeft", AppAction.PrevReview, "←", "Previous item"),
            new(KeyScope.Review, "ArrowRight", AppAction.NextReview, "→", "Next item"),
            new(KeyScope.Review, "ArrowUp", AppAction.PrevReview, "↑", "Previous item"),
            new(KeyScope.Review, "ArrowDown", AppAction.NextReview, "↓", "Next item"),
            new(KeyScope.Review, "Escape", AppAction.Escape, "Esc", "Close review"),
        ]);
        for (var p = 1; p <= 4; p++) b.Add(new(KeyScope.List, p.ToString(), AppAction.SetPriority, p.ToString(), $"Priority {p}", p));
        return b;
    }

    static readonly Dictionary<(KeyScope, string), Binding> Index = All.ToDictionary(x => (x.Scope, x.Chord));

    // Resolves a chord in a scope; modifier shortcuts fall through to the Global scope.
    public static Binding? Resolve(KeyScope scope, KeyChord chord)
    {
        var spec = chord.ToString();
        if (Index.TryGetValue((scope, spec), out var hit)) return hit;
        return scope != KeyScope.Global && Index.TryGetValue((KeyScope.Global, spec), out var global) ? global : null;
    }

    public static string? ShortcutFor(AppAction action) =>
        All.FirstOrDefault(b => b.Action == action && b.Scope == KeyScope.Global)?.Display;
}
