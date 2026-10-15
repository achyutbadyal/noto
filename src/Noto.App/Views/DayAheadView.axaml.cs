using Avalonia.Controls;
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
                _vm.NewItemRequested -= Composer.FocusInput;
            _vm = DataContext as DayAheadViewModel;
            if (_vm is not null)
                _vm.NewItemRequested += Composer.FocusInput;
        };
    }
}
