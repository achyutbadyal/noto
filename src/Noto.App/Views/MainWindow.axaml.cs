using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Noto.App.Themes;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public partial class MainWindow : Window
{
    ShellViewModel? _shell;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(30) };

    public MainWindow()
    {
        InitializeComponent();

        // Tunnel so single-key shortcuts and prompts see keys before any focused control does.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) => Attach(DataContext as ShellViewModel);
        _tick.Tick += async (_, _) => { if (_shell is not null) await _shell.TickAsync(); };
        Opened += (_, _) => _tick.Start();
        Closed += (_, _) => _tick.Stop();
    }

    void Attach(ShellViewModel? shell)
    {
        _shell = shell;
        if (shell is null) return;

        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.Selected) && shell.Selected is { } tab && Application.Current is { } app)
                ThemeBuilder.SetAccent(app, tab.Color);
        };
        shell.Appearance.Changed += ApplyAppearance;
        ApplyAppearance();
    }

    // Theme, text size, row density and the reduce-motion gate (docs/07 §11.3, §15).
    void ApplyAppearance()
    {
        if (_shell is not { } shell || Application.Current is not { } app) return;
        app.RequestedThemeVariant = ThemeBuilder.Variant(shell.Appearance.Theme);
        FontSize = shell.Appearance.BodySize;
        app.Resources["RowHeight"] = shell.Appearance.RowHeight;
        SetMotion(!ReduceMotion);
    }

    // Set by the host from IReduceMotion; transitions only run when this is false.
    public bool ReduceMotion { get; private set; }

    public void SetReduceMotion(bool reduce)
    {
        ReduceMotion = reduce;
        SetMotion(!reduce);
    }

    void SetMotion(bool on) => Classes.Set("motion", on);

    async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_shell is null || KeyChordMapper.From(e) is not { } chord) return;
        var textFocused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;
        if (await _shell.HandleKeyAsync(chord, textFocused)) e.Handled = true;
    }

    void OnScrimPressed(object? sender, PointerPressedEventArgs e) => _shell?.CommandBar.Close();
    void OnHelpScrimPressed(object? sender, PointerPressedEventArgs e) => _shell!.IsHelpOpen = false;
    void OnPanelPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true; // clicks inside a panel don't dismiss it

    async void OnResultTapped(object? sender, TappedEventArgs e)
    {
        if (_shell is not null && (sender as Control)?.DataContext is CommandResult result)
            await _shell.CommandBar.RunResultCommand.ExecuteAsync(result);
    }
}
