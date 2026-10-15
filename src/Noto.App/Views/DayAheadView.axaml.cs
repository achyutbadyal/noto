using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public partial class DayAheadView : UserControl
{
    DayAheadViewModel? _vm;

    public DayAheadView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
                _vm.NewItemRequested -= FocusAddBox;
            _vm = DataContext as DayAheadViewModel;
            if (_vm is not null)
                _vm.NewItemRequested += FocusAddBox;
        };
    }

    void FocusAddBox() => Dispatcher.UIThread.Post(() => AddBox.Focus());

    // Enter adds to this day; Escape leaves the box.
    async void OnAddKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await _vm.Add.SubmitAsync();
                break;
            case Key.Escape:
                e.Handled = true;
                _vm.Add.Dismiss();
                TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                break;
        }
    }
}
