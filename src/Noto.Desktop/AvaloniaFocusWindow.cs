using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Noto.Platform.Abstractions;

namespace Noto.Desktop;

// Always-on-top mini window: "Deploy v2.3 · 23:10 · Done" (docs/07 §7.3).
sealed class AvaloniaFocusWindow : IFocusWindow
{
    readonly Window _window;
    readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };

    public AvaloniaFocusWindow()
    {
        var done = new Button { Content = "Done" };
        done.Click += (_, _) => DoneRequested?.Invoke();

        _window = new Window
        {
            Title = "Noto focus", Width = 340, Height = 52, CanResize = false, Topmost = true, ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.BorderOnly,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new(12, 8), Children = { _label, done } },
        };
    }

    public Capability Capability => Capability.Supported;
    public event Action? DoneRequested;

    public void Show(FocusWindowState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _label.Text = $"{state.Title} · {(int)state.Remaining.TotalMinutes:00}:{state.Remaining.Seconds:00}";
            if (!_window.IsVisible) _window.Show();
        });
    }

    public void Hide() => Dispatcher.UIThread.Post(_window.Hide);
}
