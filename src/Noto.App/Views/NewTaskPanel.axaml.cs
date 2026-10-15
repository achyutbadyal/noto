using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Noto.App.ViewModels;

namespace Noto.App.Views;

// The "More options" panel of a new task: every field the domain supports. It sits over the page it is
// used on (Today, a day ahead) and is driven entirely by that page's AddItemViewModel.
public partial class NewTaskPanel : UserControl
{
    public static readonly StyledProperty<string?> SubtitleProperty = AvaloniaProperty.Register<
        NewTaskPanel,
        string?
    >(nameof(Subtitle));

    public NewTaskPanel() => InitializeComponent();

    // Says where the task will land ("Adding to Work").
    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    AddItemViewModel? Vm => DataContext as AddItemViewModel;

    // Enter submits the panel; Escape closes it. The notes box keeps Enter for new lines.
    async void OnDetailKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await vm.SubmitDetailedAsync();
                break;
            case Key.Escape:
                e.Handled = true;
                vm.Dismiss(keepTitle: true);
                break;
        }
    }

    void OnDetailedScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        Vm?.Dismiss(keepTitle: true);
    }
}
