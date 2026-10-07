using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public partial class HelpView : UserControl
{
    HelpViewModel? _vm;

    public HelpView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as HelpViewModel);
    }

    void Attach(HelpViewModel? vm)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelChanged;
        _vm = vm;
        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelChanged;
    }

    // A freshly opened topic starts at the top; otherwise picking a topic low in the rail leaves the
    // reader halfway down the previous page. Posted so it runs after the new content has been laid out.
    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HelpViewModel.Selected))
            Dispatcher.UIThread.Post(() => DocScroll.Offset = new Vector(0, 0));
    }
}
