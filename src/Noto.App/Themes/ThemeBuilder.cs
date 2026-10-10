using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Noto.App.Themes;

// Builds the Dark/Light resource dictionaries from ThemeTokens and keeps the workspace accent in sync
// at runtime, including across a theme switch.
public static class ThemeBuilder
{
    // One shared, mutable brush per accent. A binding resolves its brush once, so a theme switch has to
    // mutate the colour in place — otherwise the workspace ticks keep the old value until the next
    // refresh (the gap noted in docs/11). Mutating SolidColorBrush.Color repaints every user of it.
    static readonly Dictionary<string, SolidColorBrush> AccentBrushes = new(
        StringComparer.OrdinalIgnoreCase
    );

    public static void Apply(Application app)
    {
        app.Resources.ThemeDictionaries[ThemeVariant.Dark] = Build(ThemeTokens.Dark, dark: true);
        app.Resources.ThemeDictionaries[ThemeVariant.Light] = Build(ThemeTokens.Light, dark: false);
        app.Resources["RowHeight"] = 38.0;
        app.Resources["RadiusSmall"] = new CornerRadius(6);
        app.Resources["Radius"] = new CornerRadius(8);
        app.Resources["RadiusLarge"] = new CornerRadius(12);
        app.Resources["RadiusXLarge"] = new CornerRadius(16);
        SetAccent(app, null);
        RefreshAccentBrushes(app);
    }

    public static bool IsDark(Application? app) =>
        (app ?? Application.Current)?.ActualThemeVariant == ThemeVariant.Dark;

    // The shared brush for a workspace accent (an accent name, or a literal #rrggbb). Resolved at call
    // time, so a binding created after a theme switch already has the right colour; a switch afterwards
    // is handled by RefreshAccentBrushes.
    //
    // **UI thread only.** An Avalonia brush is an AvaloniaObject, so constructing or recolouring one from
    // a background thread throws "Call from invalid thread". Reach it from a view/converter, never from a
    // view-model constructor.
    public static IBrush AccentBrush(string? name)
    {
        var key = ThemeTokens.AccentKey(name);
        if (!AccentBrushes.TryGetValue(key, out var brush))
            AccentBrushes[key] = brush = new SolidColorBrush(Colors.Transparent);
        brush.Color = Color.Parse(ThemeTokens.AccentFor(key, IsDark(Application.Current)));
        return brush;
    }

    // Repaints every shared accent brush for the active variant, so workspace ticks follow a theme switch.
    public static void RefreshAccentBrushes(Application? app = null)
    {
        var dark = IsDark(app);
        foreach (var (key, brush) in AccentBrushes)
            brush.Color = Color.Parse(ThemeTokens.AccentFor(key, dark));
    }

    public static void SetAccent(Application app, string? accentName)
    {
        foreach (
            var (variant, dark) in new[] { (ThemeVariant.Dark, true), (ThemeVariant.Light, false) }
        )
        {
            if (app.Resources.ThemeDictionaries[variant] is not ResourceDictionary dict)
                continue;
            var color = Color.Parse(ThemeTokens.AccentFor(accentName, dark));
            dict["AccentBrush"] = new SolidColorBrush(color);
            // A soft, translucent accent used for selections and sidebar highlights (macOS "tinted" look).
            dict["AccentSoftBrush"] = new SolidColorBrush(
                Color.FromArgb(dark ? (byte)0x3A : (byte)0x24, color.R, color.G, color.B)
            );
            dict["AccentBorderBrush"] = new SolidColorBrush(
                Color.FromArgb(dark ? (byte)0x66 : (byte)0x4D, color.R, color.G, color.B)
            );
            ApplySystemAccent(dict, color);
        }
    }

    // Fluent's *own* accent resources. Without these, every framework control that draws itself with the
    // system accent — Slider, CheckBox, ToggleSwitch — stays Windows blue (#0078D7) no matter what the
    // workspace accent is. That is exactly the "one framework blue doing six jobs" problem docs/07 §11
    // exists to fix, and it is invisible in a screenshot until you look at a Slider.
    static void ApplySystemAccent(ResourceDictionary dict, Color color)
    {
        var accent = new SolidColorBrush(color);
        dict["SystemAccentColor"] = color;
        dict["SystemAccentColorLight1"] = Lighten(color, 0.15);
        dict["SystemAccentColorLight2"] = Lighten(color, 0.40);
        dict["SystemAccentColorLight3"] = Lighten(color, 0.65);
        dict["SystemAccentColorDark1"] = Darken(color, 0.25);
        dict["SystemAccentColorDark2"] = Darken(color, 0.45);
        dict["SystemAccentColorDark3"] = Darken(color, 0.70);

        dict["SystemControlHighlightAccentBrush"] = accent;
        dict["SystemControlForegroundAccentBrush"] = accent;
        dict["SystemControlBackgroundAccentBrush"] = accent;

        // Slider: the filled track and the thumb, in every state the template asks for.
        dict["SliderTrackValueFill"] = accent;
        dict["SliderTrackValueFillPointerOver"] = accent;
        dict["SliderTrackValueFillPressed"] = accent;
        dict["SliderThumbBackground"] = accent;
        dict["SliderThumbBackgroundPointerOver"] = new SolidColorBrush(Lighten(color, 0.15));

        dict["CheckBoxCheckBackgroundFillChecked"] = accent;
        dict["ToggleSwitchFillOn"] = accent;
    }

    static Color Lighten(Color c, double t) =>
        Color.FromRgb(
            (byte)(c.R + (255 - c.R) * t),
            (byte)(c.G + (255 - c.G) * t),
            (byte)(c.B + (255 - c.B) * t)
        );

    static Color Darken(Color c, double t) =>
        Color.FromRgb((byte)(c.R * (1 - t)), (byte)(c.G * (1 - t)), (byte)(c.B * (1 - t)));

    public static ThemeVariant? Variant(ViewModels.ThemeChoice choice) =>
        choice switch
        {
            ViewModels.ThemeChoice.Light => ThemeVariant.Light,
            ViewModels.ThemeChoice.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

    static ResourceDictionary Build(IReadOnlyDictionary<string, string> tokens, bool dark)
    {
        var dict = new ResourceDictionary();
        foreach (var (name, hex) in tokens)
        {
            dict[Key(name)] = Brush(hex);
            dict[Key(name) + "Color"] = Color.Parse(hex);
        }
        // A warm, translucent separator used for hairlines between chrome regions.
        dict["HairlineBrush"] = new SolidColorBrush(
            Color.FromArgb(dark ? (byte)0x24 : (byte)0x1C, 0x8A, 0x80, 0x70)
        );
        // A warm wash behind the "needs a decision" pill: an ask, not an alarm.
        var warm = Color.Parse(tokens["pressure-warm"]);
        dict["PressureWarmSoftBrush"] = new SolidColorBrush(
            Color.FromArgb(dark ? (byte)0x26 : (byte)0x22, warm.R, warm.G, warm.B)
        );
        // A 1px top highlight on raised surfaces: the "lit edge" that makes a dark UI read as layered.
        dict["LitEdgeBrush"] = new SolidColorBrush(
            dark ? Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)
        );
        return dict;
    }

    // "text-1" -> "Text1Brush", "pressure-hot" -> "PressureHotBrush"
    static string Key(string token) =>
        string.Concat(token.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..])) + "Brush";

    static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
