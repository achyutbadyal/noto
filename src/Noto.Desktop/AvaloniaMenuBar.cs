using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Noto.Platform.Abstractions;

namespace Noto.Desktop;

// Menubar / tray item: Now + timer, decisions waiting, quick add (docs/07 §7.2). Avalonia's TrayIcon + NativeMenu.
sealed class AvaloniaMenuBar : IMenuBar
{
    readonly TrayIcon _icon;
    readonly NativeMenuItem _status = new() { IsEnabled = false, Header = "Noto" };

    public AvaloniaMenuBar(Application app)
    {
        var menu = new NativeMenu();
        menu.Add(_status);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Quick add…", MenuBarAction.QuickAdd));
        menu.Add(Item("Open Today", MenuBarAction.OpenToday));
        menu.Add(Item("Start review", MenuBarAction.StartReview));
        menu.Add(Item("Shut down the day", MenuBarAction.Shutdown));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Quit Noto", MenuBarAction.Quit));

        _icon = new TrayIcon
        {
            ToolTipText = "Noto",
            Menu = menu,
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Noto.Desktop/Assets/tray.png"))),
        };
        TrayIcon.SetIcons(app, [_icon]);
    }

    public Capability Capability => Capability.Supported;
    public event Action<MenuBarAction>? ActionInvoked;

    public void Update(MenuBarState state)
    {
        var now = state.NowTitle is null ? "No Now item" : $"Now: {state.NowTitle}" + (state.Remaining is { } r ? $" · {(int)r.TotalMinutes:00}:{r.Seconds:00}" : "");
        _status.Header = state.NeedsDecision > 0 ? $"{now}  ·  {state.NeedsDecision} to decide" : now;
        _icon.ToolTipText = _status.Header;
    }

    NativeMenuItem Item(string header, MenuBarAction action)
    {
        var item = new NativeMenuItem { Header = header };
        item.Click += (_, _) => ActionInvoked?.Invoke(action);
        return item;
    }
}
