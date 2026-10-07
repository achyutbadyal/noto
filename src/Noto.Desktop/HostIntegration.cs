using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Noto.App.ViewModels;
using Noto.App.Views;
using Noto.Platform.Abstractions;

namespace Noto.Desktop;

// Everything OS-specific the main window doesn't own: hotkey + capture panel, menubar, focus window, notifications.
static class HostIntegration
{
    public static void Attach(Composition composition, ShellViewModel shell, MainWindow window, bool startWithCapture)
    {
        var services = composition.Services;
        var platform = composition.Platform;

        window.SetReduceMotion(platform.ReduceMotion.IsEnabled);
        platform.ReduceMotion.Changed += () => Dispatcher.UIThread.Post(() => window.SetReduceMotion(platform.ReduceMotion.IsEnabled));

        var capture = new CaptureController(services);
        capture.WarmUp();
        if (platform.Hotkey.Capability.IsSupported)
            platform.Hotkey.Register(HotkeyGesture.DefaultCapture, () => Dispatcher.UIThread.Post(capture.Show));
        if (startWithCapture) Dispatcher.UIThread.Post(capture.Show);

        var menu = new AvaloniaMenuBar(Application.Current!);
        menu.ActionInvoked += action => Dispatcher.UIThread.Post(async () =>
        {
            switch (action)
            {
                case MenuBarAction.QuickAdd: capture.Show(); break;
                case MenuBarAction.OpenToday: Reveal(window); await shell.GoAsync(AppPage.Today); break;
                case MenuBarAction.StartReview: Reveal(window); await shell.StartReviewAsync(); break;
                case MenuBarAction.Shutdown: Reveal(window); await shell.GoAsync(AppPage.Shutdown); break;
                case MenuBarAction.Quit: (Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(); break;
            }
        });

        var focusWindow = new AvaloniaFocusWindow();
        focusWindow.DoneRequested += () => Dispatcher.UIThread.Post(async () => await services.Focus.StopAsync());

        // Menubar, mini window and the "timer finished" notification follow the Now item.
        var notifiedFinish = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            var focus = services.Focus;
            if (focus.IsActive)
            {
                menu.Update(new MenuBarState(focus.Title, focus.Remaining, shell.TodayPage?.NeedsDecision ?? 0));
                focusWindow.Show(new FocusWindowState(focus.Title, focus.Remaining));
                if (focus.Remaining == TimeSpan.Zero && !notifiedFinish)
                {
                    notifiedFinish = true;
                    await platform.Notifications.ShowAsync(new NotificationRequest("Focus finished", focus.Title));
                }
            }
            else
            {
                notifiedFinish = false;
                focusWindow.Hide();
                menu.Update(new MenuBarState(null, null, shell.TodayPage?.NeedsDecision ?? 0));
            }
        };
        timer.Start();

        _ = MorningNudgeAsync(services, platform);
    }

    static void Reveal(MainWindow window)
    {
        window.Show();
        window.Activate();
    }

    // Opt-out nudge: only when something actually needs a decision (docs/07 §17).
    static async Task MorningNudgeAsync(Noto.App.Services.AppServices services, PlatformServices platform)
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (services.UiState.Get("notify-morning") == "off") return;

        var total = 0;
        foreach (var ws in await services.Workspaces.ListAsync())
            if (Noto.Core.Workspaces.FocusHours.IsActive(ws, services.Clock)) total += await services.Reader.NeedsDecisionCountAsync(ws.Id);
        if (total > 0)
            await platform.Notifications.ShowAsync(new NotificationRequest("Good morning", $"{total} item{(total == 1 ? "" : "s")} carried over need a decision."));
    }
}
