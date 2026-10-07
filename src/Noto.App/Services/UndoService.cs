using Noto.Core.Commands;
using Noto.Core.Time;

namespace Noto.App.Services;

public sealed record UndoEntry(string Label, IReadOnlyList<Guid> Tokens, DateTimeOffset At);

// Undo stack shared by every screen. Entries expire after 10 minutes (docs/07 principle 7).
public sealed class UndoService(ICommandBus bus, IClock clock)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    readonly List<UndoEntry> _stack = [];

    public event Action<UndoEntry>? Pushed;
    public event Action<UndoEntry>? Undone;

    public int Count
    {
        get { Prune(); return _stack.Count; }
    }

    public UndoEntry? Last
    {
        get { Prune(); return _stack.Count == 0 ? null : _stack[^1]; }
    }

    public void Push(string label, IEnumerable<Guid> tokens)
    {
        var entry = new UndoEntry(label, tokens.ToList(), clock.UtcNow);
        _stack.Add(entry);
        Pushed?.Invoke(entry);
    }

    public async Task<UndoEntry?> UndoLastAsync()
    {
        if (Last is not { } entry) return null;
        _stack.RemoveAt(_stack.Count - 1);

        // A group is undone newest-first so each snapshot restores onto the state it came from.
        foreach (var token in entry.Tokens.Reverse()) await bus.UndoAsync(token);
        Undone?.Invoke(entry);
        return entry;
    }

    void Prune() => _stack.RemoveAll(e => clock.UtcNow - e.At > Window);
}
