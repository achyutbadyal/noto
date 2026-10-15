using Avalonia.Controls;
using Avalonia.Threading;

namespace Noto.App.Views;

// Hover (or keyboard focus) opens the day's numbers after a short beat, so sweeping the pointer along
// the strip does not flicker a card per capsule. The card eases in once it is open: it is created
// hidden and given the "shown" class a frame later, so the style transition has something to run from.
public partial class DayPulseChip : UserControl
{
    static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(140);
    static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(90);
    static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(180);

    readonly DispatcherTimer _timer = new();
    bool _wantOpen;

    public DayPulseChip()
    {
        InitializeComponent();
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            if (_wantOpen)
                Open();
            else
                Close();
        };
        Hit.PointerEntered += (_, _) => Want(true);
        Hit.PointerExited += (_, _) => Want(false);
        Hit.GotFocus += (_, _) => Want(true);
        Hit.LostFocus += (_, _) => Want(false);
        DetachedFromVisualTree += (_, _) =>
        {
            _timer.Stop();
            Tip.IsOpen = false;
        };
    }

    void Want(bool open)
    {
        _wantOpen = open;
        _timer.Stop();
        // Closing a card that is already showing is quick; opening waits for the pointer to settle.
        _timer.Interval = open ? (Tip.IsOpen ? TimeSpan.Zero : OpenDelay) : CloseDelay;
        _timer.Start();
    }

    void Open()
    {
        if (Tip.IsOpen)
            return;
        Card.Classes.Set("shown", false);
        Tip.IsOpen = true;
        Dispatcher.UIThread.Post(() => Card.Classes.Set("shown", true), DispatcherPriority.Render);
    }

    void Close()
    {
        if (!Tip.IsOpen)
            return;
        Card.Classes.Set("shown", false);
        // Let the fade-out finish before the popup goes away.
        DispatcherTimer.RunOnce(
            () =>
            {
                if (!_wantOpen)
                    Tip.IsOpen = false;
            },
            Settle
        );
    }
}
