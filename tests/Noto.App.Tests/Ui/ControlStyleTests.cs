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
            var window = new Window
            {
                Width = 320,
                Height = 200,
                Content = combo,
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var part = combo
                .GetVisualDescendants()
                .OfType<Border>()
                .First(b => b.Name == "Background");
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

    // Fluent draws the Slider, CheckBox and ToggleSwitch with the *system* accent, which is Windows blue
    // (#0078D7) and ignores the workspace accent entirely — the "one framework blue doing six jobs"
    // problem docs/07 §11 exists to fix. ThemeBuilder repoints those keys; this pins the result, so a
    // renamed key or a Fluent upgrade that adds a state cannot quietly bring the blue back.
    [AvaloniaFact]
    public void Framework_controls_follow_the_workspace_accent()
    {
        var app = Application.Current!;
        var failures = new List<string>();

        try
        {
            foreach (
                var (name, variant, expected) in new[]
                {
                    ("dark", ThemeVariant.Dark, ThemeTokens.AccentFor("teal", dark: true)),
                    ("light", ThemeVariant.Light, ThemeTokens.AccentFor("teal", dark: false)),
                }
            )
            {
                app.RequestedThemeVariant = variant;
                ThemeBuilder.SetAccent(app, "teal");

                foreach (var key in FrameworkAccentKeys)
                {
                    var brush = app.TryFindResource(key, variant, out var value)
                        ? value as ISolidColorBrush
                        : null;
                    if (brush?.Color != Color.Parse(expected))
                        failures.Add($"{name} {key}: {brush?.Color} != {expected}");
                }

                // The derived tints must stay distinct, or the pointer-over and pressed states stop
                // reading as a change.
                var baseColor = app.TryFindResource("SystemAccentColor", variant, out var b)
                    ? b as Color?
                    : null;
                var light1 = app.TryFindResource("SystemAccentColorLight1", variant, out var l)
                    ? l as Color?
                    : null;
                if (baseColor is null || light1 is null || baseColor == light1)
                    failures.Add($"{name} SystemAccentColor tints are not distinct");
            }
        }
        finally
        {
            // The headless app is shared across the assembly: leave the accent as we found it.
            ThemeBuilder.SetAccent(app, null);
        }

        failures.ShouldBeEmpty();
    }

    static readonly string[] FrameworkAccentKeys =
    [
        "SliderTrackValueFill",
        "SliderTrackValueFillPointerOver",
        "SliderTrackValueFillPressed",
        "SliderThumbBackground",
        "CheckBoxCheckBackgroundFillChecked",
        "ToggleSwitchFillOn",
    ];
}
