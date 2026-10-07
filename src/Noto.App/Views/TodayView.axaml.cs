using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Noto.App.Logic;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public partial class TodayView : UserControl
{
    TodayViewModel? _vm;

    public TodayView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.NewItemRequested -= FocusAddBox;
            _vm = DataContext as TodayViewModel;
            if (_vm is not null) _vm.NewItemRequested += FocusAddBox;
        };
    }

    void FocusAddBox() => Dispatcher.UIThread.Post(() => AddBox.Focus());

    // Enter adds; Backspace at the end of a recognized token removes the whole token; Escape leaves the box.
    async void OnAddKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await _vm.Add.SubmitAsync();
                break;
            case Key.Escape:
                e.Handled = true;
                _vm.Add.Text = "";
                TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                break;
            case Key.Back when AddBox.SelectionStart == AddBox.SelectionEnd && AddBox.CaretIndex == (AddBox.Text?.Length ?? 0):
                var before = _vm.Add.Text;
                var after = TokenParser.RemoveTrailingToken(before, _vm.Snapshot?.Today ?? default);
                if (before.Length - after.Length > 1)
                {
                    e.Handled = true;
                    _vm.Add.Text = after;
                    AddBox.CaretIndex = after.Length;
                }
                break;
        }
    }
}
