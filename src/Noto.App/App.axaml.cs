using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Noto.App.Themes;
using Noto.App.ViewModels;
using Noto.App.Views;

namespace Noto.App;

public sealed class App : Application
{
    // Set by the host before the framework starts; the app itself knows nothing about storage or the OS.
    public static Func<ShellViewModel>? ShellFactory { get; set; }

    // Raised once the main window exists so the host can attach tray icons, hotkeys, etc.
    public static event Action<ShellViewModel, MainWindow>? WindowCreated;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ThemeBuilder.Apply(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (
            ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && ShellFactory is not null
        )
        {
            var shell = ShellFactory();
            var window = new MainWindow { DataContext = shell };
            desktop.MainWindow = window;
            WindowCreated?.Invoke(shell, window);
            _ = shell.InitializeAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
