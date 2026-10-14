using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Noto.App.ViewModels;

namespace Noto.App.Views;

// Calendar drag-and-drop. Dropping a card on a day (or the Unscheduled tray) runs exactly the plan command
// the keyboard nudge does, so undo and the toast behave identically (docs/07 §9.1).
public partial class CalendarView : UserControl
{
    CalendarViewModel? _vm;
    ItemRowViewModel? _candidate;
    Point _origin;

    public CalendarView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => _vm = DataContext as CalendarViewModel;
        WeekScroll.SizeChanged += (_, e) => FitWeek(e.NewSize.Width);
    }

    // A day column never gets narrower than it can hold a card in; below that the week scrolls sideways
    // instead of squeezing. An explicit width is needed because a scroller measures its child unbounded.
    const double MinDayColumn = 172;

    void FitWeek(double viewport) => WeekGrid.Width = Math.Max(MinDayColumn * 7, viewport - 2);

    static T? FindData<T>(object? source)
        where T : class
    {
        var node = source as Visual;
        while (node is not null)
        {
            if (node is Control { DataContext: T hit })
                return hit;
            node = node.GetVisualParent();
        }
        return null;
    }

    void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _origin = e.GetPosition(this);
        _candidate = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            ? FindData<ItemRowViewModel>(e.Source)
            : null;
    }

    async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_candidate is not { } row)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _candidate = null;
            return;
        }

        // A small threshold, so a click that focuses a card is not read as a drag.
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _origin.X) < 6 && Math.Abs(point.Y - _origin.Y) < 6)
            return;

        _candidate = null;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(ItemDrag.Payload, row.Id.ToString()));
        await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
    }

    // Past days are closed: no drop effect, so the cursor shows it before the pointer is released.
    void OnDayDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = (sender as Control)?.DataContext is SectionViewModel { IsDropTarget: true }
            ? DragDropEffects.Move
            : DragDropEffects.None;

    async void OnDayDrop(object? sender, DragEventArgs e)
    {
        if (_vm is null)
            return;
        if ((sender as Control)?.DataContext is not SectionViewModel { IsDropTarget: true } target)
            return;
        if (e.DataTransfer?.TryGetValue(ItemDrag.Payload) is not { } payload)
            return;
        if (!Guid.TryParse(payload, out var id))
            return;

        var row = _vm.FlatRows.FirstOrDefault(r => r.Id == id);
        if (row is not null)
            await _vm.MoveToDayAsync(row.Item, target.Day);
    }

    void OnOpenDay(object? sender, RoutedEventArgs e)
    {
        if (
            _vm is null
            || (sender as Control)?.DataContext is not SectionViewModel { Day: { } day }
        )
            return;
        _vm.OpenWeek(day);
    }
}
