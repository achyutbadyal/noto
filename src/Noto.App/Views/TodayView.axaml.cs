using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noto.App.Logic;
using Noto.App.ViewModels;
using Noto.Core.Text;

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

    void FocusAddBox() => Composer.FocusInput();

    // Any dismissal (Escape, the scrim, a click outside) drops focus so the caret stops blinking.
    void OnAddDismissed() =>
        Dispatcher.UIThread.Post(() => TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus());

    // A press outside the capture bar leaves the field, keeping whatever was typed.
    void OnSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null || e.Source is not Visual source)
            return;
        if (IsWithin(source, Composer.Bar) || IsWithin(source, DetailedPanel))
            return;
        if (!_vm.Add.IsDetailedOpen && _vm.Add.Text.Length == 0 && !Composer.IsInputFocused)
            return;
        _vm.Add.Dismiss(keepTitle: true);
    }

    // Dropdown lists are hosted in an overlay, outside the visual tree of the control that opened them,
    // but they stay in its logical tree. So a press on one of their items still counts as inside.
    static bool IsWithin(Visual node, Visual ancestor) =>
        ReferenceEquals(node, ancestor)
        || node.GetVisualAncestors().Contains(ancestor)
        || node.GetLogicalAncestors().Contains(ancestor);
}
