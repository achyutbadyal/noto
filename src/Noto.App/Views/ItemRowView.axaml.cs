using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public partial class ItemRowView : UserControl
{
    // Below this a row cannot hold the full rail and still show a readable title.
    const double NarrowBelow = 380;

    public ItemRowView()
    {
        InitializeComponent();
        RowRoot.SizeChanged += (_, e) =>
        {
            if (Row is { } row)
                row.IsNarrow = e.NewSize.Width < NarrowBelow;
        };
    }

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

    async void OnOpenLink(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } button)
            await LinkLauncher.OpenAsync(button, url);
    }

    // The one-line link summary under a title opens the first link.
    async void OnOpenFirstLink(object? sender, PointerPressedEventArgs e)
    {
        if (Row is { Links: [{ } first, ..] } && sender is Visual v)
        {
            e.Handled = true;
            await LinkLauncher.OpenAsync(v, first.Url);
        }
    }

    void OnDoubleTapped(object? sender, TappedEventArgs e) => Row?.EditCommand.Execute(null);
}
