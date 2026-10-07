using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Noto.App.Themes;

// Builds the Dark/Light resource dictionaries from ThemeTokens and swaps the workspace accent at runtime.
public static class ThemeBuilder
{
    public static void Apply(Application app)
    {
        app.Resources.ThemeDictionaries[ThemeVariant.Dark] = Build(ThemeTokens.Dark, dark: true);
        app.Resources.ThemeDictionaries[ThemeVariant.Light] = Build(ThemeTokens.Light, dark: false);
        app.Resources["RowHeight"] = 36.0;
        SetAccent(app, null);
    }

    public static void SetAccent(Application app, string? accentName)
    {
        foreach (var (variant, dark) in new[] { (ThemeVariant.Dark, true), (ThemeVariant.Light, false) })
        {
            if (app.Resources.ThemeDictionaries[variant] is ResourceDictionary dict)
                dict["AccentBrush"] = Brush(ThemeTokens.AccentFor(accentName, dark));
        }
    }

    public static ThemeVariant? Variant(ViewModels.ThemeChoice choice) => choice switch
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
        return dict;
    }

    // "text-1" -> "Text1Brush", "pressure-hot" -> "PressureHotBrush"
    static string Key(string token) =>
        string.Concat(token.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..])) + "Brush";

    static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
