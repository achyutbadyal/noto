using System.Runtime.Versioning;
using Noto.Platform.Abstractions;

namespace Noto.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class WindowsPlatform
{
    const string NoContext =
        "Capture with context isn't available on Windows. Noto can't read the foreground app's page.";

    public static PlatformServices Create() =>
        new(
            new WinHotkey(),
            new WinKeyring(),
            new UnsupportedCaptureContext(NoContext),
            new WinNotifications(),
            new PolledReduceMotion(ClientAreaAnimationsOff, TimeSpan.FromSeconds(5))
        );

    // Settings > Accessibility > Visual effects > Animation effects.
    static bool ClientAreaAnimationsOff() =>
        WinNative.SystemParametersInfoW(WinNative.SpiGetClientAreaAnimation, 0, out var on, 0)
        && on == 0;
}
