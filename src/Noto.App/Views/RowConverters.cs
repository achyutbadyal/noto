using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public static class RowConverters
{
    // Resolves a stored icon name (or a legacy emoji) to a monochrome geometry for PathIcon.
    public static readonly IValueConverter Icon = new FuncValueConverter<string?, Geometry?>(
        AppIcons.Get
    );

    // Drives overlay fades without toggling IsVisible (which would skip the transition).
    public static readonly IValueConverter BoolToOpacity = new FuncValueConverter<bool, double>(
        open => open ? 1.0 : 0.0
    );
    public static readonly IValueConverter InverseBool = new FuncValueConverter<bool, bool>(value =>
        !value
    );

    // Enter transitions: the command palette drops in and the toast slides up, both via Margin
    // (this Avalonia build has no transform transition, but ThicknessTransition gives the same read).
    public static readonly IValueConverter PanelMargin = new FuncValueConverter<bool, Thickness>(
        open => new Thickness(0, open ? 120 : 108, 0, 0)
    );
    public static readonly IValueConverter ToastMargin = new FuncValueConverter<bool, Thickness>(
        visible => new Thickness(0, 0, 0, visible ? 28 : 12)
    );

    public static readonly IValueConverter TodayOpacity = new FuncValueConverter<bool, double>(
        today => today ? 1.0 : 0.55
    );

    // Sparkline bars: a 0..1 fraction becomes a bar height, so a trend reads as a shape.
    public static readonly IValueConverter SparkHeight = new FuncValueConverter<double, double>(
        fraction => 4 + Math.Clamp(fraction, 0, 1) * 24
    );

    // The review card leaves toward the decision: ←defer, ↑someday, ↓drop, →today (docs/07 §4.1).
    public static readonly IValueConverter ExitMargin = new FuncValueConverter<string?, Thickness>(
        dir =>
            dir switch
            {
                "left" => new Thickness(-30, 0, 30, 0),
                "right" => new Thickness(30, 0, -30, 0),
                "up" => new Thickness(0, -22, 0, 22),
                "down" => new Thickness(0, 22, 0, -22),
                _ => new Thickness(0),
            }
    );

    public static readonly IValueConverter ExitOpacity = new FuncValueConverter<string?, double>(
        dir => string.IsNullOrEmpty(dir) ? 1.0 : 0.25
    );

    // The inspector collapses to zero width (instead of IsVisible) so its width can animate.
    public static readonly IValueConverter InspectorWidth = new FuncValueConverter<bool, double>(
        show => show ? 320 : 0
    );

    // The key is rendered as a kbd chip beside the label, not baked into the string.
    public static readonly IValueConverter CloseLabel = new FuncValueConverter<bool, string>(
        complete => complete ? "Start the day" : "Skip review"
    );

    public static readonly IValueConverter IsDoneStep = new FuncValueConverter<ShutdownStep, bool>(
        s => s == ShutdownStep.Done
    );
    public static readonly IValueConverter IsNotDoneStep = new FuncValueConverter<
        ShutdownStep,
        bool
    >(s => s == ShutdownStep.NotDone);
    public static readonly IValueConverter IsNoteStep = new FuncValueConverter<ShutdownStep, bool>(
        s => s == ShutdownStep.Note
    );

    // Workspace accent names resolve to a shared, mutable brush, so a theme switch repaints every
    // workspace tick in place instead of waiting for the next refresh (docs/11 known gap).
    public static readonly IValueConverter Accent = new FuncValueConverter<string?, IBrush?>(name =>
        Themes.ThemeBuilder.AccentBrush(name)
    );

    public static readonly IValueConverter SelectedBackground = new FuncValueConverter<
        bool,
        IBrush?
    >(selected =>
        selected
        && Avalonia.Application.Current?.TryGetResource(
            "HoverBrush",
            Avalonia.Application.Current.ActualThemeVariant,
            out var b
        ) == true
            ? b as IBrush
            : Brushes.Transparent
    );

    public static readonly IValueConverter IsWins = new FuncValueConverter<WeeklyStep, bool>(s =>
        s == WeeklyStep.Wins
    );
    public static readonly IValueConverter IsSomeday = new FuncValueConverter<WeeklyStep, bool>(s =>
        s == WeeklyStep.Someday
    );
    public static readonly IValueConverter IsStuck = new FuncValueConverter<WeeklyStep, bool>(s =>
        s == WeeklyStep.Stuck
    );
    public static readonly IValueConverter IsEstimates = new FuncValueConverter<WeeklyStep, bool>(
        s => s == WeeklyStep.Estimates
    );
    public static readonly IValueConverter IsNextWeek = new FuncValueConverter<WeeklyStep, bool>(
        s => s == WeeklyStep.NextWeek
    );

    public static readonly IValueConverter Strike = new FuncValueConverter<
        bool,
        TextDecorationCollection?
    >(done => done ? TextDecorations.Strikethrough : null);
}
