using Noto.Core.Links;

namespace Noto.Providers.Preview;

// Lower value = fetched sooner (visible rows, then Waiting/Today, then the rest).
public enum PreviewPriority { Visible = 0, WaitingOrToday = 1, Rest = 2 }

public sealed record PreviewRequest(string Url, PreviewPriority Priority = PreviewPriority.Rest, bool Force = false);

// A state-hash change observed on refresh; input to the live-link rules.
public sealed record LinkChange(string Url, LinkState? From, LinkState? To, string? FromHash, string ToHash, LinkPreview Preview);

public sealed record RefreshResult(IReadOnlyDictionary<string, LinkPreview> Previews, IReadOnlyList<LinkChange> Changes);

public sealed class PreviewOptions
{
    public TimeSpan MinInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan BackoffBase { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan BackoffMax { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan Debounce { get; init; } = TimeSpan.FromMilliseconds(500);
}

public interface IDelay
{
    Task DelayAsync(TimeSpan time, CancellationToken ct);
}

public sealed class SystemDelay : IDelay
{
    public Task DelayAsync(TimeSpan time, CancellationToken ct) => Task.Delay(time, ct);
}

// Collapses bursts of edits (typing into a title) into one action after a quiet period.
public sealed class Debouncer(IDelay delay, TimeSpan quiet)
{
    readonly Dictionary<string, CancellationTokenSource> _pending = [];

    public async Task RunAsync(string key, Func<Task> action)
    {
        CancellationTokenSource cts;
        lock (_pending)
        {
            if (_pending.Remove(key, out var previous)) previous.Cancel();
            _pending[key] = cts = new CancellationTokenSource();
        }

        try { await delay.DelayAsync(quiet, cts.Token); }
        catch (OperationCanceledException) { return; } // superseded by a newer edit

        lock (_pending)
        {
            if (!_pending.TryGetValue(key, out var current) || current != cts) return;
            _pending.Remove(key);
        }
        await action();
    }
}
