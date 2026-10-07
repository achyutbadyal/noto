namespace Noto.Platform.Abstractions;

// Platform features are optional: callers check Capability and show `Reason` instead of hiding the feature.
public sealed record Capability(bool IsSupported, string? Reason = null)
{
    public static readonly Capability Supported = new(true);

    public static Capability Unsupported(string reason) => new(false, reason);
}

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Command = 8,
}

public sealed record HotkeyGesture(string Key, HotkeyModifiers Modifiers)
{
    // ⌃⌥Space on macOS, Ctrl+Alt+Space elsewhere (docs/07 §7.2).
    public static readonly HotkeyGesture DefaultCapture = new(
        "Space",
        HotkeyModifiers.Control | HotkeyModifiers.Alt
    );
}

public interface IGlobalHotkey : IDisposable
{
    Capability Capability { get; }
    bool Register(HotkeyGesture gesture, Action onPressed);
    void Unregister();
}

public enum MenuBarAction
{
    QuickAdd,
    OpenToday,
    StartReview,
    Shutdown,
    Quit,
}

public sealed record MenuBarState(string? NowTitle, TimeSpan? Remaining, int NeedsDecision);

public interface IMenuBar
{
    Capability Capability { get; }
    void Update(MenuBarState state);
    event Action<MenuBarAction>? ActionInvoked;
}

public interface IKeyring
{
    Capability Capability { get; }
    Task SetAsync(string service, string account, string secret);
    Task<string?> GetAsync(string service, string account);
    Task DeleteAsync(string service, string account);
}

public sealed record CaptureContext(string AppName, string? Url, string? PageTitle);

public interface ICaptureContext
{
    Capability Capability { get; }

    // The frontmost app and, for browsers, its current page. Null when nothing useful is available.
    Task<CaptureContext?> GetAsync();
}

public sealed record NotificationRequest(string Title, string Body);

public interface INotifications
{
    Capability Capability { get; }
    Task ShowAsync(NotificationRequest request);
}

public interface IReduceMotion
{
    bool IsEnabled { get; }
    event Action? Changed;
}

public sealed record FocusWindowState(string Title, TimeSpan Remaining);

public interface IFocusWindow
{
    Capability Capability { get; }
    void Show(FocusWindowState state);
    void Hide();
    event Action? DoneRequested;
}

// Everything the app needs from the OS, resolved once at startup.
public sealed record PlatformServices(
    IGlobalHotkey Hotkey,
    IKeyring Keyring,
    ICaptureContext CaptureContext,
    INotifications Notifications,
    IReduceMotion ReduceMotion
);
