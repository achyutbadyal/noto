using System.Globalization;

namespace Noto.Core.Sync;

// Hybrid logical clock timestamp "<unix_ms>:<counter>:<device_id>". Total order: ms, counter, device.
public readonly record struct Hlc(long Ms, int Counter, Guid Device) : IComparable<Hlc>
{
    public static Hlc Zero(Guid device) => new(0, 0, device);

    public int CompareTo(Hlc other)
    {
        var c = Ms.CompareTo(other.Ms);
        if (c != 0)
            return c;
        c = Counter.CompareTo(other.Counter);
        return c != 0 ? c : Device.CompareTo(other.Device);
    }

    public static bool operator >(Hlc a, Hlc b) => a.CompareTo(b) > 0;

    public static bool operator <(Hlc a, Hlc b) => a.CompareTo(b) < 0;

    public override string ToString() => $"{Ms}:{Counter}:{Device}";

    public static bool TryParse(string? text, out Hlc hlc)
    {
        hlc = default;
        var parts = text?.Split(':');
        if (parts is not { Length: 3 })
            return false;
        if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ms))
            return false;
        if (
            !int.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var counter
            )
        )
            return false;
        if (!Guid.TryParse(parts[2], out var device))
            return false;
        hlc = new Hlc(ms, counter, device);
        return true;
    }

    public static Hlc Parse(string text) =>
        TryParse(text, out var h) ? h : throw new FormatException($"Invalid HLC '{text}'");

    // Compares two serialized clocks; a missing clock sorts below everything.
    public static int Compare(string? a, string? b)
    {
        if (a is null)
            return b is null ? 0 : -1;
        if (b is null)
            return 1;
        return Parse(a).CompareTo(Parse(b));
    }
}

// Monotonic per device, and advances on receive so a slow clock can't keep losing against peers.
public sealed class HybridClock(Func<long> wallClockMs, Guid device)
{
    readonly object _lock = new();
    long _ms;
    int _counter;

    public Guid Device => device;

    public Hlc Next()
    {
        lock (_lock)
        {
            var wall = wallClockMs();
            if (wall > _ms)
            {
                _ms = wall;
                _counter = 0;
            }
            else
                _counter++;
            return new Hlc(_ms, _counter, device);
        }
    }

    public void Receive(Hlc remote)
    {
        lock (_lock)
        {
            var wall = wallClockMs();
            var max = Math.Max(Math.Max(wall, _ms), remote.Ms);
            if (max == _ms && max == remote.Ms)
                _counter = Math.Max(_counter, remote.Counter);
            else if (max == remote.Ms)
                _counter = remote.Counter;
            else if (max != _ms)
                _counter = -1; // wall clock moved ahead; Next() will reset
            _ms = max;
        }
    }

    // Restores the last persisted clock so a restart can't reissue timestamps.
    public void Restore(string? serialized)
    {
        if (!Hlc.TryParse(serialized, out var last))
            return;
        lock (_lock)
        {
            if (last.Ms > _ms || (last.Ms == _ms && last.Counter > _counter))
            {
                _ms = last.Ms;
                _counter = last.Counter;
            }
        }
    }

    public string Last
    {
        get
        {
            lock (_lock)
                return new Hlc(_ms, Math.Max(_counter, 0), device).ToString();
        }
    }
}
