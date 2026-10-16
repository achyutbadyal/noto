using Noto.Platform.Abstractions;
using Noto.Platform.Linux;
using Noto.Platform.MacOS;
using Noto.Platform.Windows;

namespace Noto.Platform;

public static class PlatformFactory
{
    // The keyring is the file-backed store for every platform: the OS keyring stays reachable as the
    // `OsKeyring` mode and as the one-time source for lazy migration, but it is no longer what the app
    // writes by default (docs/05 › ADR-06).
    public static PlatformServices Create(string dataDirectory, string deviceId)
    {
        var store = new FileKeyring(dataDirectory, deviceId, CreateOsKeyring());

        if (OperatingSystem.IsMacOS())
            return new PlatformServices(
                new MacHotkey(),
                store,
                new MacCaptureContext(),
                new MacNotifications(),
                new MacReduceMotion(),
                store
            );
        if (OperatingSystem.IsWindows())
            return WindowsPlatform.Create(store);
        if (OperatingSystem.IsLinux())
            return LinuxPlatform.Create(store);
        return Unsupported(store);
    }

    static IKeyring CreateOsKeyring()
    {
        if (OperatingSystem.IsMacOS())
            return new MacKeyring();
        if (OperatingSystem.IsWindows())
            return new WinKeyring();
        if (OperatingSystem.IsLinux())
            return SecretToolKeyring.Create();
        return new InMemoryKeyring();
    }

    // FreeBSD and anything else Avalonia might run on: every feature explains that it is missing.
    static PlatformServices Unsupported(FileKeyring store)
    {
        const string reason = "This platform isn't supported yet.";
        return new PlatformServices(
            new UnsupportedHotkey(reason),
            store,
            new UnsupportedCaptureContext(reason),
            new UnsupportedNotifications(reason),
            new StaticReduceMotion(),
            store
        );
    }
}
