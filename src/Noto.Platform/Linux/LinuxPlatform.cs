using Noto.Platform.Abstractions;

namespace Noto.Platform.Linux;

// Phase 12: libsecret keyring, X11 hotkey and portal-based hotkeys are not implemented yet.
public static class LinuxPlatform
{
    public static PlatformServices Create() =>
        new(
            new UnsupportedHotkey(
                "Global hotkeys aren't available yet. Bind a system shortcut to `noto --capture` instead."
            ),
            new InMemoryKeyring(),
            new UnsupportedCaptureContext("Capture with context isn't available on Linux."),
            new UnsupportedNotifications("Desktop notifications aren't available on Linux yet."),
            new StaticReduceMotion()
        );
}
