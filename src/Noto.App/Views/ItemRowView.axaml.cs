using Avalonia.Controls;
using Avalonia.Input;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public partial class ItemRowView : UserControl
{
    public ItemRowView() => InitializeComponent();

    ItemRowViewModel? Row => DataContext as ItemRowViewModel;

    void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Row is not { Owner: { } owner } row)
            return;
        var meta =
            e.KeyModifiers.HasFlag(KeyModifiers.Meta)
            || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (meta)
            row.IsSelected = !row.IsSelected;
        else
            owner.ClearSelection();
        owner.SetFocus(row);
    }

    void OnDoubleTapped(object? sender, TappedEventArgs e) => Row?.EditCommand.Execute(null);
}
