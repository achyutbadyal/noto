using Noto.App.Logic;
using Noto.App.Themes;

namespace Noto.App.Tests;

// Acceptance (docs/08 Phase 3): both themes pass the contrast table, computed from the tokens themselves.
public class ThemeTests
{
    public static IEnumerable<object[]> Themes => [[ "dark", ThemeTokens.Dark ], [ "light", ThemeTokens.Light ]];

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_tokens_reach_AA_on_canvas_surface_and_raised(string theme, IReadOnlyDictionary<string, string> tokens)
    {
        var failures = new List<string>();
        foreach (var fg in ThemeTokens.TextTokens)
        foreach (var bg in ThemeTokens.Backgrounds)
        {
            var ratio = Contrast.Ratio(tokens[fg], tokens[bg]);
            if (ratio < 4.5) failures.Add($"{theme}: {fg} on {bg} = {ratio:0.00}");
        }
        failures.ShouldBeEmpty();
    }

    [Fact]
    public void Dark_text_3_on_hover_is_the_documented_exception()
    {
        // Spec: dark text-3 on hover is 4.4:1, so hovered rows render metadata in text-2 instead.
        Contrast.Ratio(ThemeTokens.Dark["text-3"], ThemeTokens.Dark["hover"]).ShouldBeLessThan(4.5);
        Contrast.Ratio(ThemeTokens.Dark["text-2"], ThemeTokens.Dark["hover"]).ShouldBeGreaterThanOrEqualTo(4.5);

        foreach (var fg in ThemeTokens.TextTokens.Where(t => t != "text-3"))
            Contrast.Ratio(ThemeTokens.Dark[fg], ThemeTokens.Dark["hover"]).ShouldBeGreaterThanOrEqualTo(4.5, fg);
        foreach (var fg in ThemeTokens.TextTokens)
            Contrast.Ratio(ThemeTokens.Light[fg], ThemeTokens.Light["hover"]).ShouldBeGreaterThanOrEqualTo(4.5, fg);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Both_themes_define_the_same_tokens(string theme, IReadOnlyDictionary<string, string> tokens) =>
        tokens.Keys.OrderBy(k => k).ShouldBe(ThemeTokens.Dark.Keys.OrderBy(k => k), theme);

    [Fact]
    public void Workspace_accents_reach_3_to_1_for_non_text_use()
    {
        var failures = new List<string>();
        foreach (var (name, (dark, light)) in ThemeTokens.Accents)
        {
            foreach (var bg in new[] { "canvas", "surface" })
            {
                if (Contrast.Ratio(dark, ThemeTokens.Dark[bg]) < 3) failures.Add($"dark {name} on {bg}");
                if (Contrast.Ratio(light, ThemeTokens.Light[bg]) < 3) failures.Add($"light {name} on {bg}");
            }
        }
        failures.ShouldBeEmpty();
        ThemeTokens.Accents.Count.ShouldBe(10);
    }

    [Fact]
    public void Contrast_math_matches_wcag_reference_values()
    {
        Contrast.Ratio("#000000", "#FFFFFF").ShouldBe(21.0, 0.01);
        Contrast.Ratio("#777777", "#FFFFFF").ShouldBe(4.48, 0.02);
        Contrast.Ratio("#FFFFFF", "#FFFFFF").ShouldBe(1.0, 0.001);
    }

    [Fact]
    public void Accent_lookup_falls_back_sensibly()
    {
        ThemeTokens.AccentFor("teal", dark: true).ShouldBe("#3CCBB8");
        ThemeTokens.AccentFor("teal", dark: false).ShouldBe("#0F7F70");
        ThemeTokens.AccentFor("#123456", dark: true).ShouldBe("#123456");
        ThemeTokens.AccentFor("nonsense", dark: true).ShouldBe(ThemeTokens.AccentFor("blue", dark: true));
    }
}
