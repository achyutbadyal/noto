using System.Runtime.Versioning;
using System.Security;
using System.Text;
using Microsoft.Win32;
using Noto.Platform.Abstractions;
using Noto.Platform.Internal;

namespace Noto.Platform.Windows;

// Toasts for an unpackaged app. The AppUserModelID is registered under HKCU so the toast is attributed to "Noto"
// rather than to PowerShell, and the toast itself is raised through Windows PowerShell, which ships with Windows.
[SupportedOSPlatform("windows")]
public sealed class WinNotifications : INotifications
{
    static readonly object Gate = new();
    static bool _registered;

    public Capability Capability => Capability.Supported;

    public async Task ShowAsync(NotificationRequest request)
    {
        RegisterAppId();
        var script = ToastScript.Build(request);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        await ProcessRunner.RunAsync(
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand", encoded]
        );
    }

    static void RegisterAppId()
    {
        lock (Gate)
        {
            if (_registered)
                return;
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(
                    $@"Software\Classes\AppUserModelId\{ToastScript.AppId}"
                );
                key.SetValue("DisplayName", "Noto");
            }
            catch (Exception e) when (e is UnauthorizedAccessException or SecurityException)
            {
                // Toasts still show, attributed to the host process.
            }
            _registered = true;
        }
    }
}

// The PowerShell that raises a toast. Platform-neutral so its escaping can be tested anywhere.
static class ToastScript
{
    public const string AppId = "app.noto";

    public static string Build(NotificationRequest request)
    {
        // The toast XML is a PowerShell single-quoted string, so both XML and quote escaping apply.
        var xml =
            "<toast><visual><binding template=\"ToastGeneric\">"
            + $"<text>{SecurityElement.Escape(request.Title)}</text>"
            + $"<text>{SecurityElement.Escape(request.Body)}</text>"
            + "</binding></visual></toast>";
        return string.Join(
            '\n',
            "$ErrorActionPreference = 'Stop'",
            "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null",
            "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null",
            "$doc = New-Object Windows.Data.Xml.Dom.XmlDocument",
            $"$doc.LoadXml('{xml.Replace("'", "''")}')",
            "$toast = [Windows.UI.Notifications.ToastNotification]::new($doc)",
            $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{AppId}').Show($toast)"
        );
    }
}
