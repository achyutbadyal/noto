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
            if (_subscribed is not null)
                _subscribed.FocusChanged -= ScrollToFocus;
            _subscribed = DataContext as ItemListViewModel;
            if (_subscribed is not null)
                _subscribed.FocusChanged += ScrollToFocus;
        };
    }

    void OnSectionHeaderClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SectionViewModel { IsCollapsible: true } section)
            _subscribed?.ToggleSection(section);
    }

    // Keep the keyboard-focused row on screen. The list virtualizes, so an off-screen row has no view to
    // bring into view; the ScrollViewer is nudged to its offset instead.
    void ScrollToFocus(ItemRowViewModel? row)
    {
        if (row is null)
            return;
        Dispatcher.UIThread.Post(
            () =>
            {
                var view = this.GetVisualDescendants()
                    .OfType<ItemRowView>()
                    .FirstOrDefault(r => r.DataContext == row);
                if (view is not null)
                {
                    view.BringIntoView();
                    return;
                }

                if (_subscribed is not { } vm)
                    return;
                var entries = vm.FlatEntries.ToList();
                var index = entries.IndexOf(row);
                if (index < 0 || entries.Count == 0)
                    return;
                var fraction = index / (double)entries.Count;
                Scroller.Offset = new Avalonia.Vector(
                    Scroller.Offset.X,
                    fraction * Math.Max(0, Scroller.Extent.Height - Scroller.Viewport.Height)
                );
            },
            DispatcherPriority.Background
        );
    }
}
