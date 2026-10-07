using Noto.Platform.Abstractions;

namespace Noto.Platform.Windows;

// Phase 12: RegisterHotKey, Credential Manager (DPAPI) and toast notifications are not implemented yet.
public static class WindowsPlatform
{
    const string Pending = "Not available in this build of Noto for Windows yet.";

    public static PlatformServices Create() => new(
        new UnsupportedHotkey(Pending), new InMemoryKeyring(), new UnsupportedCaptureContext(Pending),
        new UnsupportedNotifications(Pending), new StaticReduceMotion());
}
