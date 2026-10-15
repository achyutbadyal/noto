using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using Noto.App.ViewModels;

namespace Noto.App.Views;

// Card drag for the Calendar and the Board. It is pointer-driven rather than Avalonia's DragDrop because
// the platform owns the drag image there: on macOS it is a generic document icon that cannot be replaced.
// Here the ghost is a real, theme-aware card that follows the pointer, and the lane under it lights up.
//
// A drop zone is a Border carrying the `dayCell` or `column` class whose DataContext is a SectionViewModel.
sealed class ItemDragger
{
    const double Threshold = 6;

    readonly Control _host;
    readonly Func<SectionViewModel, bool> _canDrop;
    readonly Func<ItemRowViewModel, SectionViewModel, Task> _drop;

    ItemRowViewModel? _candidate;
    Point _origin;
    Size _sourceSize;
    Point _grab;
    bool _dragging;
    Border? _ghost;
    OverlayLayer? _layer;
    Border? _hover;

    public ItemDragger(
        Control host,
        Func<SectionViewModel, bool> canDrop,
        Func<ItemRowViewModel, SectionViewModel, Task> drop
    )
    {
        _host = host;
        _canDrop = canDrop;
        _drop = drop;
        host.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        host.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        host.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        host.AddHandler(
            InputElement.PointerCaptureLostEvent,
            (_, _) => Cancel(),
            RoutingStrategies.Direct
        );
        host.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

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

    static Control? FindControl(object? source)
    {
        var node = source as Visual;
        while (node is not null)
        {
            if (node is Control { DataContext: ItemRowViewModel } c && c is UserControl)
                return c;
            node = node.GetVisualParent();
        }
        return null;
    }

    static Border? ZoneAt(Visual? node)
    {
        while (node is not null)
        {
            if (
                node is Border b
                && (b.Classes.Contains("dayCell") || b.Classes.Contains("column"))
                && b.DataContext is SectionViewModel
            )
                return b;
            node = node.GetVisualParent();
        }
        return null;
    }

    void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _origin = e.GetPosition(_host);
        _candidate = e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed
            ? FindData<ItemRowViewModel>(e.Source)
            : null;
        if (_candidate is { IsEditing: true })
            _candidate = null;

        // Remember the card's footprint and where it was gripped, so the ghost is the same width and
        // stays under the same spot of the pointer.
        if (_candidate is not null && FindControl(e.Source) is { } source)
        {
            _sourceSize = source.Bounds.Size;
            _grab = e.GetPosition(source);
        }
    }

    void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging)
        {
            e.Handled = true;
            Track(e);
            return;
        }
        if (_candidate is not { } row)
            return;
        if (!e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed)
        {
            _candidate = null;
            return;
        }

        // A small threshold, so a click that focuses a card is not read as a drag.
        var point = e.GetPosition(_host);
        if (Math.Abs(point.X - _origin.X) < Threshold && Math.Abs(point.Y - _origin.Y) < Threshold)
            return;

        _layer = OverlayLayer.GetOverlayLayer(_host);
        if (_layer is null)
        {
            _candidate = null;
            return;
        }
        _dragging = true;
        e.Pointer.Capture(_host);
        _ghost = BuildGhost(row, _sourceSize.Width);
        _layer.Children.Add(_ghost);
        _host.Cursor = new Cursor(StandardCursorType.DragMove);
        e.Handled = true;
        Track(e);
    }

    static Border BuildGhost(ItemRowViewModel row, double width)
    {
        var ghost = new Border
        {
            Width = width > 0 ? width : 240,
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Opacity = 0.96,
            BoxShadow = BoxShadows.Parse("0 10 28 0 #55000000"),
            Child = new TextBlock
            {
                Text = row.Title,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
            },
        };
        ghost[!Border.BackgroundProperty] = new DynamicResourceExtension("RaisedBrush");
        ghost[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
        ((TextBlock)ghost.Child)[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(
            "Text1Brush"
        );
        return ghost;
    }

    void Track(PointerEventArgs e)
    {
        if (_ghost is null || _layer is null)
            return;
        var at = e.GetPosition(_layer);
        Canvas.SetLeft(_ghost, at.X - _grab.X);
        Canvas.SetTop(_ghost, at.Y - Math.Min(_grab.Y, 24));

        var zone = ZoneAt(_host.InputHitTest(e.GetPosition(_host)) as Visual);
        var target = zone is { DataContext: SectionViewModel s } && _canDrop(s) ? zone : null;
        if (ReferenceEquals(target, _hover))
            return;
        _hover?.Classes.Set("dropHover", false);
        _hover = target;
        _hover?.Classes.Set("dropHover", true);
        _host.Cursor = new Cursor(
            target is null ? StandardCursorType.No : StandardCursorType.DragMove
        );
    }

    async void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
        {
            _candidate = null;
            return;
        }
        e.Handled = true;
        var row = _candidate;
        var zone = _hover;
        Cancel();
        if (row is not null && zone?.DataContext is SectionViewModel section)
            await _drop(row, section);
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dragging && e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel();
        }
    }

    void Cancel()
    {
        if (!_dragging && _ghost is null)
            return;
        _dragging = false;
        _candidate = null;
        _hover?.Classes.Set("dropHover", false);
        _hover = null;
        if (_ghost is not null)
            _layer?.Children.Remove(_ghost);
        _ghost = null;
        _host.Cursor = Cursor.Default;
    }
}
