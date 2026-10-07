namespace Noto.Platform.Abstractions;

public sealed class UnsupportedHotkey(string reason) : IGlobalHotkey
{
    public Capability Capability { get; } = Capability.Unsupported(reason);

    public bool Register(HotkeyGesture gesture, Action onPressed) => false;

    public void Unregister() { }

    public void Dispose() { }
}

public sealed class UnsupportedCaptureContext(string reason) : ICaptureContext
{
    public Capability Capability { get; } = Capability.Unsupported(reason);

    public Task<CaptureContext?> GetAsync() => Task.FromResult<CaptureContext?>(null);
}

public sealed class UnsupportedNotifications(string reason) : INotifications
{
    public Capability Capability { get; } = Capability.Unsupported(reason);

    public Task ShowAsync(NotificationRequest request) => Task.CompletedTask;
}

public sealed class StaticReduceMotion(bool enabled = false) : IReduceMotion
{
    public bool IsEnabled { get; } = enabled;
    public event Action? Changed
    {
        add { }
        remove { }
    }
}
