using Noto.Platform.Abstractions;
using Noto.Platform.Linux;
using Noto.Platform.MacOS;
using Noto.Platform.Windows;

namespace Noto.Platform;

public static class PlatformFactory
{
    public static PlatformServices Create()
    {
        if (OperatingSystem.IsMacOS())
            return new PlatformServices(
                new MacHotkey(),
                new MacKeyring(),
                new MacCaptureContext(),
                new MacNotifications(),
                new MacReduceMotion()
            );
        if (OperatingSystem.IsWindows())
            return WindowsPlatform.Create();
        if (OperatingSystem.IsLinux())
            return LinuxPlatform.Create();
        return Unsupported();
    }

    // FreeBSD and anything else Avalonia might run on: every feature explains that it is missing.
    static PlatformServices Unsupported()
    {
        const string reason = "This platform isn't supported yet.";
        return new PlatformServices(
            new UnsupportedHotkey(reason),
            new InMemoryKeyring(),
            new UnsupportedCaptureContext(reason),
            new UnsupportedNotifications(reason),
            new StaticReduceMotion()
        );
    }
}
