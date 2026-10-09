namespace Noto.Platform.Abstractions;

// For platforms that expose the setting only as a value to read, not as a change notification.
public sealed class PolledReduceMotion : IReduceMotion, IDisposable
{
    readonly Func<bool> _read;
    readonly Timer _poll;
    bool _last;

    public PolledReduceMotion(Func<bool> read, TimeSpan interval)
    {
        _read = read;
        _last = Read();
        _poll = new Timer(_ => Poll(), null, interval, interval);
    }

    public bool IsEnabled => _last;
    public event Action? Changed;

    public void Dispose() => _poll.Dispose();

    void Poll()
    {
        var now = Read();
        if (now == _last)
            return;
        _last = now;
        Changed?.Invoke();
    }

    bool Read()
    {
        try
        {
            return _read();
        }
        catch (Exception)
        {
            return _last; // a failed read never flips the setting
        }
    }
}
