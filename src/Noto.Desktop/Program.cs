using Avalonia;
using NotoApp = Noto.App.App;
using Noto.App.Views;
using Noto.Platform.Abstractions;

namespace Noto.Desktop;

static class Program
{
    static Composition? _composition;

    [STAThread]
    public static int Main(string[] args)
    {
        _composition = new Composition(DataDirectoryFrom(args));
        NotoApp.ShellFactory = _composition.CreateShell;
        NotoApp.WindowCreated += (shell, window) => HostIntegration.Attach(_composition, shell, window, startWithCapture: args.Contains("--capture"));

        try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally { _composition.Dispose(); }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<NotoApp>().UsePlatformDetect().WithInterFont().LogToTrace();

    // `--data-dir <path>` keeps development databases away from the real one.
    static string? DataDirectoryFrom(string[] args)
    {
        var i = Array.IndexOf(args, "--data-dir");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
