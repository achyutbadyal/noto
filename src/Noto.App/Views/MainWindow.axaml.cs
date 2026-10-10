using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

        // Same for presses: clicking a row or a blank area has to take focus out of a text field.
        AddHandler(PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) => Attach(DataContext as ShellViewModel);
        SizeChanged += OnSizeChanged;

        // "System" follows the OS: when the platform flips variant, repaint the accents for it.
        if (Application.Current is { } currentApp)
            currentApp.ActualThemeVariantChanged += (_, _) => ReapplyAccents();
        _tick.Tick += async (_, _) =>
        {
            if (_shell is not null)
                await _shell.TickAsync();
        };
        Opened += (_, _) =>
        {
            _tick.Start();
            ApplyViewport(Bounds.Width);
        };
        Closed += (_, _) => _tick.Stop();
    }

    // Drives the responsive shell: the sidebar collapses and the inspector hides as the window narrows.
    // The minimum width guards against the tiny/zero sizes reported during the first layout pass, which
    // would otherwise flash a collapsed layout and animate the inspector in on startup.
    void OnSizeChanged(object? sender, SizeChangedEventArgs e) => ApplyViewport(e.NewSize.Width);

    void ApplyViewport(double width)
    {
        if (_shell is not null && width >= MinViewportWidth)
            _shell.ViewportWidth = width;
    }

    const double MinViewportWidth = 320;

    void Attach(ShellViewModel? shell)
    {
        if (_shell is not null)
        {
            _shell.PropertyChanged -= OnShellPropertyChanged;
            _shell.CommandBar.PropertyChanged -= OnCommandBarChanged;
        }
        _shell = shell;
        if (shell is null)
            return;

        shell.PropertyChanged += OnShellPropertyChanged;
        shell.CommandBar.PropertyChanged += OnCommandBarChanged;
        shell.Appearance.Changed += ApplyAppearance;
        ApplyAppearance();
    }

    // The command bar's overlay fades (Opacity + IsHitTestVisible) instead of toggling IsVisible, so a
    // focus-when-visible behaviour never fires — it has to be focused when it opens, or ⌘K leaves the
    // caret wherever it was and typing goes to the wrong box.
    void OnCommandBarChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CommandBarViewModel.IsOpen))
            return;
        if (_shell?.CommandBar.IsOpen == true)
        {
            Dispatcher.UIThread.Post(() =>
            {
                BarBox.Focus();
                BarBox.CaretIndex = BarBox.Text?.Length ?? 0;
            });
            return;
        }
        // Closing has to give focus back, or the field stays focused (while invisible) and single-key
        // list shortcuts keep being treated as typing.
        if (BarBox.IsFocused)
            Dispatcher.UIThread.Post(() => TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus());
    }

    void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_shell is not { } shell)
            return;
        if (
            e.PropertyName == nameof(ShellViewModel.Selected)
            && shell.Selected is { } tab
            && Application.Current is { } app
        )
            ThemeBuilder.SetAccent(app, tab.Color);
        else if (e.PropertyName == nameof(ShellViewModel.Content))
            FadeInPage();
    }

    // New page fades in on its own (the old page is swapped out instantly, so there's no muddy cross-fade).
    void FadeInPage()
    {
        if (ReduceMotion || !IsVisible)
            return;
        PageHost.Opacity = 0;
        Dispatcher.UIThread.Post(() => PageHost.Opacity = 1, DispatcherPriority.Render);
    }

    // Theme, text size, row density and the reduce-motion gate (docs/07 §11.3, §15).
    void ApplyAppearance()
    {
        if (_shell is not { } shell || Application.Current is not { } app)
            return;
        app.RequestedThemeVariant = ThemeBuilder.Variant(shell.Appearance.Theme);
        FontSize = shell.Appearance.BodySize;
        app.Resources["RowHeight"] = shell.Appearance.RowHeight;
        SetMotion(!ReduceMotion);
        ReapplyAccents();
    }

    // Accents are theme-dependent, so both the global accent resources and the shared per-workspace
    // brushes have to be re-derived whenever the variant changes — including when "System" follows the
    // OS to a new variant, which is why this also runs on ActualThemeVariantChanged (docs/11 gap).
    void ReapplyAccents()
    {
        if (_shell is not { } shell || Application.Current is not { } app)
            return;
        ThemeBuilder.SetAccent(app, shell.Selected?.Color);
        ThemeBuilder.RefreshAccentBrushes(app);
    }

    // Set by the host from IReduceMotion; transitions only run when this is false.
    public bool ReduceMotion { get; private set; }

    public void SetReduceMotion(bool reduce)
    {
        ReduceMotion = reduce;
        SetMotion(!reduce);
    }

    // The "motion" class turns on every transition in Styles.axaml.
    void SetMotion(bool on) => Classes.Set("motion", on);

    async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_shell is null || KeyChordMapper.From(e) is not { } chord)
            return;
        var textFocused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;
        if (await _shell.HandleKeyAsync(chord, textFocused))
            e.Handled = true;
    }

    void OnScrimPressed(object? sender, PointerPressedEventArgs e) => _shell?.CommandBar.Close();

    // Clicking outside a text field leaves it, the way every native app behaves. Rows and panels are not
    // focusable, so without this the caret stayed in whatever box was last used. The press is not
    // consumed: whatever was clicked still gets it (and focuses itself if it can).
    void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source)
            return;
        if (source.GetSelfAndVisualAncestors().OfType<TextBox>().Any())
            return;
        if (TopLevel.GetTopLevel(this)?.FocusManager is not { } focus)
            return;
        if (focus.GetFocusedElement() is TextBox)
            focus.ClearFocus();
    }

    void OnHelpScrimPressed(object? sender, PointerPressedEventArgs e) =>
        _shell!.IsHelpOpen = false;

    void OnPanelPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true; // clicks inside a panel don't dismiss it

    async void OnResultTapped(object? sender, TappedEventArgs e)
    {
        if (_shell is not null && (sender as Control)?.DataContext is CommandResult result)
            await _shell.CommandBar.RunResultCommand.ExecuteAsync(result);
    }
}
