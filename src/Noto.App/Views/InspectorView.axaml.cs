using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public partial class InspectorView : UserControl
{
    public InspectorView() => InitializeComponent();

    async void OnNotesLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm)
            await vm.SaveNotesAsync();
    }

    // The title and the waiting-on name commit on Enter or when focus leaves, never per keystroke.
    async void OnTitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is InspectorViewModel vm)
        {
            e.Handled = true;
            await vm.CommitTitleAsync();
        }
    }

    async void OnTitleLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm)
            await vm.CommitTitleAsync();
    }

    async void OnWaitingKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is InspectorViewModel vm)
        {
            e.Handled = true;
            await vm.WaitingChangedAsync();
        }
    }

    async void OnWaitingLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm)
            await vm.WaitingChangedAsync();
    }

    // Opens the link in the default browser. Only web links are launched; the scanner never produces others.
    async void OnOpenLinkClick(object? sender, RoutedEventArgs e)
    {
        if (
            sender is Button { Tag: string url }
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && TopLevel.GetTopLevel(this) is { } top
        )
            await top.Launcher.LaunchUriAsync(uri);
    }
}
