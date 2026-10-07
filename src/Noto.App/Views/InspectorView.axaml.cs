using Avalonia.Controls;
using Avalonia.Interactivity;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public partial class InspectorView : UserControl
{
    public InspectorView() => InitializeComponent();

    async void OnNotesLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm) await vm.SaveNotesAsync();
    }
}
