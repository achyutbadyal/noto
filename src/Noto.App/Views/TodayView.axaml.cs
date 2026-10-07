using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noto.App.Logic;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public partial class TodayView : UserControl
{
    TodayViewModel? _vm;

    public TodayView()
    {
        InitializeComponent();

        // Tunnel so a press anywhere is seen before a row button swallows it: clicking away from the
        // capture bar has to leave it, and only Escape used to work.
        AddHandler(PointerPressedEvent, OnSurfacePointerPressed, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) => Attach(DataContext as TodayViewModel);
    }

    void Attach(TodayViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.NewItemRequested -= FocusAddBox;
            _vm.Add.Dismissed -= OnAddDismissed;
        }
        _vm = vm;
        if (_vm is not null)
        {
            _vm.NewItemRequested += FocusAddBox;
            _vm.Add.Dismissed += OnAddDismissed;
        }
    }

    void FocusAddBox() => Dispatcher.UIThread.Post(() => AddBox.Focus());

    // Any dismissal (Escape, the scrim, a click outside) drops focus so the caret stops blinking.
    void OnAddDismissed() =>
        Dispatcher.UIThread.Post(() => TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus());

    // A press outside the capture bar leaves the field, keeping whatever was typed.
    void OnSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null || e.Source is not Visual source)
            return;
        if (IsWithin(source, CaptureBar) || IsWithin(source, DetailedPanel))
            return;
        if (!_vm.Add.IsDetailedOpen && _vm.Add.Text.Length == 0 && !AddBox.IsFocused)
            return;
        _vm.Add.Dismiss(keepTitle: true);
    }

    static bool IsWithin(Visual? node, Visual ancestor)
    {
        for (var current = node; current is not null; current = current.GetVisualParent())
            if (ReferenceEquals(current, ancestor))
                return true;
        return false;
    }

    // Enter adds; Backspace at the end of a recognized token removes the whole token; Escape leaves the box.
    async void OnAddKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await _vm.Add.SubmitAsync();
                break;
            case Key.Escape:
                e.Handled = true;
                _vm.Add.Dismiss();
                break;
            case Key.Back
                when AddBox.SelectionStart == AddBox.SelectionEnd
                    && AddBox.CaretIndex == (AddBox.Text?.Length ?? 0):
                var before = _vm.Add.Text;
                var after = TokenParser.RemoveTrailingToken(
                    before,
                    _vm.Snapshot?.Today ?? default,
                    recognizeTags: false
                );
                if (before.Length - after.Length > 1)
                {
                    e.Handled = true;
                    _vm.Add.Text = after;
                    AddBox.CaretIndex = after.Length;
                }
                break;
        }
    }

    // Enter submits the panel; Escape closes it. The notes box keeps Enter for new lines.
    async void OnDetailKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await _vm.Add.SubmitDetailedAsync();
                break;
            case Key.Escape:
                e.Handled = true;
                _vm.Add.Dismiss(keepTitle: true);
                break;
        }
    }

    void OnDetailedScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        _vm?.Add.Dismiss(keepTitle: true);
    }
}
