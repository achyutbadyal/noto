using System.Runtime.Versioning;
using Noto.Platform.Abstractions;

namespace Noto.Platform.MacOS;

// Frontmost app + browser page via Apple Events. macOS asks for Automation permission on first use.
[SupportedOSPlatform("macos")]
public sealed class MacCaptureContext : ICaptureContext
{
    static readonly Dictionary<string, string> Browsers = new()
    {
        ["Safari"] =
            "tell application \"Safari\" to return (URL of front document) & linefeed & (name of front document)",
        ["Google Chrome"] =
            "tell application \"Google Chrome\" to return (URL of active tab of front window) & linefeed & (title of active tab of front window)",
        ["Arc"] =
            "tell application \"Arc\" to return (URL of active tab of front window) & linefeed & (title of active tab of front window)",
        ["Brave Browser"] =
            "tell application \"Brave Browser\" to return (URL of active tab of front window) & linefeed & (title of active tab of front window)",
    };

    public Capability Capability => Capability.Supported;

    public async Task<CaptureContext?> GetAsync()
    {
        var app = await AppleScript.RunAsync(
            "tell application \"System Events\" to return name of first application process whose frontmost is true"
        );
        if (string.IsNullOrEmpty(app))
            return null;
        if (!Browsers.TryGetValue(app, out var script))
            return new CaptureContext(app, null, null);

        var page = await AppleScript.RunAsync(script);
        var parts = page?.Split('\n', 2);
        return parts is { Length: 2 } && Uri.TryCreate(parts[0], UriKind.Absolute, out _)
            ? new CaptureContext(app, parts[0], parts[1])
            : new CaptureContext(app, null, null);
    }
}
