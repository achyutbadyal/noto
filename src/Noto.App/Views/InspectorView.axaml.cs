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
}
