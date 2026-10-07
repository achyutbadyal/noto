using System.Runtime.Versioning;
using Noto.Platform.Abstractions;

namespace Noto.Platform.MacOS;

[SupportedOSPlatform("macos")]
public sealed class MacNotifications : INotifications
{
    public Capability Capability => Capability.Supported;

    public async Task ShowAsync(NotificationRequest request) =>
        await AppleScript.RunAsync(
            $"display notification {AppleScript.Quote(request.Body)} with title {AppleScript.Quote(request.Title)}");
}
