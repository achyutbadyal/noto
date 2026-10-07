using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public partial class CaptureWindow : Window
{
    public CaptureWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CaptureViewModel vm)
            return;
        var chord =
            e.Key == Key.Tab
                ? new Logic.KeyChord("Tab", Shift: e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                : KeyChordMapper.From(e);
        if (chord is { } c && await vm.HandleKeyAsync(c))
            e.Handled = true;
    }

    void OnWorkspaceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (
            DataContext is CaptureViewModel vm
            && e.AddedItems.OfType<WorkspaceChoice>().FirstOrDefault() is { } choice
            && vm.Selected != choice
        )
            vm.Select(choice);
    }
}
