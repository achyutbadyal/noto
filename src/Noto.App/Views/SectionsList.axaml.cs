using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public partial class SectionsList : UserControl
{
    ItemListViewModel? _subscribed;

    public SectionsList()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_subscribed is not null) _subscribed.FocusChanged -= ScrollToFocus;
            _subscribed = DataContext as ItemListViewModel;
            if (_subscribed is not null) _subscribed.FocusChanged += ScrollToFocus;
        };
    }

    void OnSectionHeaderClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SectionViewModel { IsCollapsible: true } section) section.IsCollapsed = !section.IsCollapsed;
    }

    // Keep the keyboard-focused row on screen.
    void ScrollToFocus(ItemRowViewModel? row)
    {
        if (row is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            var view = this.GetVisualDescendants().OfType<ItemRowView>().FirstOrDefault(r => r.DataContext == row);
            view?.BringIntoView();
        }, DispatcherPriority.Background);
    }
}
