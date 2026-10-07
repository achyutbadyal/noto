using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noto.App.Themes;

namespace Noto.App.Tests.Ui;

// Regression: Fluent's dropdown state brushes are translucent overlays, so a hovered dropdown went grey
// on white and near-black on dark. Styles.axaml repoints the resource keys at our palette; this pins the
// rendered result against ThemeTokens, so a renamed key, a changed Fluent template, or a hex that drifts
// from the tokens is caught here.
public sealed class ControlStyleTests
{
    [AvaloniaFact]
    public void Dropdown_states_use_our_palette_in_both_themes()
    {
        var failures = new List<string>();
        foreach (
            var (name, variant, tokens) in new[]
            {
                ("dark", ThemeVariant.Dark, ThemeTokens.Dark),
                ("light", ThemeVariant.Light, ThemeTokens.Light),
            }
        )
        {
            Application.Current!.RequestedThemeVariant = variant;

            var combo = new ComboBox { ItemsSource = new[] { "a", "b" }, SelectedIndex = 0 };
            var window = new Window { Width = 320, Height = 200, Content = combo };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var part = combo.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Background");
            Check(name, "normal", part, tokens, "raised", failures);

            ((IPseudoClasses)combo.Classes).Set(":pointerover", true);
            Dispatcher.UIThread.RunJobs();
            Check(name, "hover", part, tokens, "hover", failures);

            ((IPseudoClasses)combo.Classes).Set(":pressed", true);
            Dispatcher.UIThread.RunJobs();
            Check(name, "pressed", part, tokens, "border", failures);
        }
        failures.ShouldBeEmpty();
    }

    static void Check(
        string theme,
        string state,
        Border part,
        IReadOnlyDictionary<string, string> tokens,
        string token,
        List<string> failures
    )
    {
        var actual = (part.Background as ISolidColorBrush)?.Color;
        var expected = Color.Parse(tokens[token]);
        if (actual != expected)
            failures.Add($"{theme} {state}: {actual} != {token} {expected}");
    }
}
