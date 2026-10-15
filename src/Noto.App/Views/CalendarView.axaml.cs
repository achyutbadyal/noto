using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Noto.App.ViewModels;

namespace Noto.App.Views;

// Calendar drag-and-drop (see ItemDragger). Dropping a card on a day (or the Unscheduled tray) runs exactly
// the plan command the keyboard nudge does, so undo and the toast behave identically (docs/07 §9.1).
public partial class CalendarView : UserControl
{
    CalendarViewModel? _vm;

    public CalendarView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => _vm = DataContext as CalendarViewModel;
        WeekScroll.SizeChanged += (_, e) => FitWeek(e.NewSize.Width);
        // Past days are closed: no drop highlight, so the lane shows it before the pointer is released.
        _ = new ItemDragger(
            this,
            section => section.IsDropTarget,
            async (row, target) =>
            {
                if (_vm is not null)
                    await _vm.MoveToDayAsync(row.Item, target.Day);
            }
        );
    }

    // A day column never gets narrower than it can hold a card in; below that the week scrolls sideways
    // instead of squeezing. An explicit width is needed because a scroller measures its child unbounded.
    const double MinDayColumn = 220;

    void FitWeek(double viewport) => WeekGrid.Width = Math.Max(MinDayColumn * 7, viewport - 2);

    void OnOpenDay(object? sender, RoutedEventArgs e)
    {
        if (
            _vm is null
            || (sender as Control)?.DataContext is not SectionViewModel { Day: { } day }
        )
            return;
        _vm.OpenWeek(day);
    }
}
