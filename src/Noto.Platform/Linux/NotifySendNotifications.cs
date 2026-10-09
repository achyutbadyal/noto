using Noto.Platform.Abstractions;
using Noto.Platform.Internal;

namespace Noto.Platform.Linux;

// freedesktop notifications through libnotify's `notify-send`.
public sealed class NotifySendNotifications(string executable) : INotifications
{
    public Capability Capability => Capability.Supported;

    public static INotifications Create() =>
        ProcessRunner.FindOnPath("notify-send") is { } path
            ? new NotifySendNotifications(path)
            : new UnsupportedNotifications(
                "Desktop notifications need `notify-send` (libnotify-bin) and a notification daemon."
            );

    public async Task ShowAsync(NotificationRequest request) =>
        await ProcessRunner.RunAsync(executable, Arguments(request));

    // `--` ends option parsing, so a title that starts with a dash is still text.
    internal static string[] Arguments(NotificationRequest request) =>
        ["--app-name=Noto", "--", request.Title, request.Body];
}
