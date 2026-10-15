using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Noto.App.Views;

// The one way the app opens a web link. Only http(s) is ever launched; the scanner never produces others.
public static class LinkLauncher
{
    public static bool CanOpen(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    public static async Task OpenAsync(Visual from, string? url)
    {
        if (
            CanOpen(url)
            && TopLevel.GetTopLevel(from) is { } top
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        )
            await top.Launcher.LaunchUriAsync(uri);
    }
}
