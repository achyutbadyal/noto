using System.Runtime.Versioning;
using Noto.Platform.Abstractions;
using Noto.Platform.Internal;

namespace Noto.Platform.Linux;

[SupportedOSPlatform("linux")]
public static class LinuxPlatform
{
    public static PlatformServices Create() =>
        new(
            new X11Hotkey(),
            SecretToolKeyring.Create(),
            new UnsupportedCaptureContext(
                "Capture with context isn't available on Linux: there is no portable way to read the focused app."
            ),
            NotifySendNotifications.Create(),
            new PolledReduceMotion(AnimationsDisabled, TimeSpan.FromSeconds(30))
        );

    // GNOME's "Animations" switch. Other desktops have no equivalent key, so the setting reads as off.
    static bool AnimationsDisabled()
    {
        if (ProcessRunner.FindOnPath("gsettings") is not { } gsettings)
            return false;
        var result = ProcessRunner
            .RunAsync(
                gsettings,
                ["get", "org.gnome.desktop.interface", "enable-animations"],
                timeout: TimeSpan.FromSeconds(2)
            )
            .GetAwaiter()
            .GetResult();
        return result.Succeeded && result.Output.Trim() == "false";
    }
}
