using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Noto.App.ViewModels;

namespace Noto.App.Views;

// Board drag-and-drop. Dragging a card onto a column runs exactly the same command the keyboard move
// does, so the WIP check and the undo toast behave identically (docs/07 §9.1).
public partial class BoardView : UserControl
{
    // The drag payload: an item id under an application-scoped format.
    static readonly DataFormat<string> ItemPayload = DataFormat.CreateStringApplicationFormat(
        "noto/item"
    );

    BoardViewModel? _vm;
    ItemRowViewModel? _candidate;
    Point _origin;

    public BoardView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => _vm = DataContext as BoardViewModel;
    }

    // The row under the pointer: walk up from the event source until a control carries the model.
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
        transfer.Add(DataTransferItem.Create(ItemPayload, row.Id.ToString()));
        await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
    }

    void OnColumnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = (sender as Control)?.DataContext is SectionViewModel { ColumnId: not null }
            ? DragDropEffects.Move
            : DragDropEffects.None;

    async void OnColumnDrop(object? sender, DragEventArgs e)
    {
        if (_vm is null)
            return;
        if ((sender as Control)?.DataContext is not SectionViewModel { ColumnId: { } columnId })
            return;
        if (e.DataTransfer?.TryGetValue(ItemPayload) is not { } payload)
            return;
        if (!Guid.TryParse(payload, out var id))
            return;

        var row = _vm.FlatRows.FirstOrDefault(r => r.Id == id);
        if (row is not null)
            await _vm.MoveToColumnAsync(row.Item, columnId);
    }
}
