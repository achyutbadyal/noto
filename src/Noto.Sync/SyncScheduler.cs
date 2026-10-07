namespace Noto.Sync;

public enum SyncStatus { Idle, Syncing, Offline }

// Debounced 2s after a change, every 60s while foreground, immediately on reconnect (docs/05).
// Errors are swallowed into `Offline`: sync must never block or break the app.
public sealed class SyncScheduler : IAsyncDisposable
{
    readonly SyncClient _client;
    readonly SyncOptions _options;
    readonly ITimer _debounce;
    readonly ITimer _interval;
    readonly SemaphoreSlim _gate = new(1, 1);

    public SyncScheduler(SyncClient client, TimeProvider time, SyncOptions? options = null)
    {
        _client = client;
        _options = options ?? new SyncOptions();
        _debounce = time.CreateTimer(_ => _ = RunAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _interval = time.CreateTimer(_ => _ = RunAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public SyncStatus Status { get; private set; } = SyncStatus.Idle;
    public Exception? LastError { get; private set; }
    public event Action<SyncStatus>? StatusChanged;

    public void Start() => _interval.Change(_options.Interval, _options.Interval);
    public void Stop() => _interval.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    public void NotifyChanged() => _debounce.Change(_options.Debounce, Timeout.InfiniteTimeSpan);
    public void NotifyReconnected() => _ = RunAsync();

    public async Task RunAsync()
    {
        if (!await _gate.WaitAsync(0)) return; // a run is already in flight
        try
        {
            Set(SyncStatus.Syncing);
            await _client.SyncAsync();
            LastError = null;
            Set(SyncStatus.Idle);
        }
        catch (Exception e)
        {
            LastError = e;
            Set(SyncStatus.Offline);
        }
        finally { _gate.Release(); }
    }

    void Set(SyncStatus status)
    {
        if (Status == status) return;
        Status = status;
        StatusChanged?.Invoke(status);
    }

    public async ValueTask DisposeAsync()
    {
        await _debounce.DisposeAsync();
        await _interval.DisposeAsync();
        _gate.Dispose();
    }
}
