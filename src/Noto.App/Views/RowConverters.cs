using Avalonia.Data.Converters;
using Avalonia.Media;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public static class RowConverters
{
    public static readonly IValueConverter TodayOpacity = new FuncValueConverter<bool, double>(today => today ? 1.0 : 0.55);

    public static readonly IValueConverter CloseLabel = new FuncValueConverter<bool, string>(complete => complete ? "Start the day  Esc" : "Skip review  Esc");

    public static readonly IValueConverter IsDoneStep = new FuncValueConverter<ShutdownStep, bool>(s => s == ShutdownStep.Done);
    public static readonly IValueConverter IsNotDoneStep = new FuncValueConverter<ShutdownStep, bool>(s => s == ShutdownStep.NotDone);
    public static readonly IValueConverter IsNoteStep = new FuncValueConverter<ShutdownStep, bool>(s => s == ShutdownStep.Note);

    // Workspace accent names resolve per theme variant at bind time.
    public static readonly IValueConverter Accent = new FuncValueConverter<string?, IBrush?>(name =>
        new SolidColorBrush(Color.Parse(Themes.ThemeTokens.AccentFor(name, Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark))));

    public static readonly IValueConverter SelectedBackground = new FuncValueConverter<bool, IBrush?>(selected =>
        selected && Avalonia.Application.Current?.TryGetResource("HoverBrush", Avalonia.Application.Current.ActualThemeVariant, out var b) == true ? b as IBrush : Brushes.Transparent);

    public static readonly IValueConverter IsWins = new FuncValueConverter<WeeklyStep, bool>(s => s == WeeklyStep.Wins);
    public static readonly IValueConverter IsSomeday = new FuncValueConverter<WeeklyStep, bool>(s => s == WeeklyStep.Someday);
    public static readonly IValueConverter IsStuck = new FuncValueConverter<WeeklyStep, bool>(s => s == WeeklyStep.Stuck);
    public static readonly IValueConverter IsEstimates = new FuncValueConverter<WeeklyStep, bool>(s => s == WeeklyStep.Estimates);
    public static readonly IValueConverter IsNextWeek = new FuncValueConverter<WeeklyStep, bool>(s => s == WeeklyStep.NextWeek);

    public static readonly IValueConverter Strike = new FuncValueConverter<bool, TextDecorationCollection?>(done => done ? TextDecorations.Strikethrough : null);
}
