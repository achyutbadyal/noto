using Avalonia.Controls;
using Avalonia.Input;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public partial class BacklogView : UserControl
{
    public BacklogView() => InitializeComponent();

    async void OnAddKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not BacklogViewModel vm) return;
        if (e.Key == Key.Enter) { e.Handled = true; await vm.Add.SubmitAsync(); }
        else if (e.Key == Key.Escape) { e.Handled = true; vm.Add.Text = ""; TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus(); }
    }
}
