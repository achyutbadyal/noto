using System.Collections.Concurrent;

namespace Noto.Server.Middleware;

// Small fixed-window counter for limits the built-in limiter can't express (per user *and* provider).
public sealed class RateGate(TimeProvider time)
{
    readonly ConcurrentDictionary<string, (long Window, int Count)> _windows = new();

    public bool TryAcquire(string key, int limit, TimeSpan window)
    {
        var current = time.GetUtcNow().Ticks / window.Ticks;
        var entry = _windows.AddOrUpdate(
            key,
            _ => (current, 1),
            (_, e) => e.Window == current ? (current, e.Count + 1) : (current, 1)
        );

        if (_windows.Count > 10_000) // opportunistic cleanup of stale windows
            foreach (var kv in _windows.Where(kv => kv.Value.Window < current))
                _windows.TryRemove(kv.Key, out _);
        return entry.Count <= limit;
    }
}
