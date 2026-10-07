using Avalonia.Threading;
using Noto.App.Services;
using Noto.App.ViewModels;
using Noto.App.Views;

namespace Noto.Desktop;

// Keeps the capture window built and hidden so hotkey -> saved stays under 2s (docs/07 §7.2).
sealed class CaptureController(AppServices services)
{
    CaptureWindow? _window;
    CaptureViewModel? _vm;

    public void WarmUp()
    {
        _vm = new CaptureViewModel(services);
        _vm.CloseRequested += () => _window?.Hide();
        _window = new CaptureWindow { DataContext = _vm };
    }

    public async void Show()
    {
        if (_window is null || _vm is null)
            WarmUp();
        await _vm!.PrepareAsync();
        _vm.Add.Text = "";
        _window!.Show();
        _window.Activate();
        Dispatcher.UIThread.Post(() => _window.Focus());
    }
}
