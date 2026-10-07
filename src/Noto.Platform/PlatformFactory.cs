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
        return OperatingSystem.IsWindows() ? WindowsPlatform.Create() : LinuxPlatform.Create();
    }
}
