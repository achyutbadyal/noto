namespace Noto.App.Themes;

// Design tokens from docs/07 §11.1. Single source: the Avalonia resource dictionaries are built from these,
// and the contrast tests read the same values.
public static class ThemeTokens
{
    public static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string>
    {
        ["canvas"] = "#0E0F12",
        ["surface"] = "#16181D",
        ["raised"] = "#1D2027",
        ["hover"] = "#242833",
        ["border"] = "#2A2E38",
        ["text-1"] = "#ECEEF2",
        ["text-2"] = "#A3A9B6",
        ["text-3"] = "#868D9B",
        ["link"] = "#7AA7FF",
        ["done"] = "#6CC08B",
        ["pressure-warm"] = "#E0B252",
        ["pressure-hot"] = "#F08A4B",
        ["pressure-stale"] = "#FF6B6B",
    };

    public static readonly IReadOnlyDictionary<string, string> Light = new Dictionary<string, string>
    {
        ["canvas"] = "#F7F7F5",
        ["surface"] = "#FFFFFF",
        ["raised"] = "#F0F0EC",
        ["hover"] = "#EFEFEB",
        ["border"] = "#E2E2DC",
        ["text-1"] = "#17181C",
        ["text-2"] = "#525866",
        ["text-3"] = "#676D7A",
        ["link"] = "#2F5FD0",
        ["done"] = "#2B7A4B",
        ["pressure-warm"] = "#8A5F00",
        ["pressure-hot"] = "#B34A12",
        ["pressure-stale"] = "#C22F2F",
    };

    // Foreground tokens that must reach WCAG AA (4.5:1) on every background token below.
    public static readonly IReadOnlyList<string> TextTokens =
        ["text-1", "text-2", "text-3", "link", "done", "pressure-warm", "pressure-hot", "pressure-stale"];

    public static readonly IReadOnlyList<string> Backgrounds = ["canvas", "surface", "raised"];

    // Ten workspace accents, tuned per theme. Used for bars and accents (non-text, 3:1).
    public static readonly IReadOnlyDictionary<string, (string Dark, string Light)> Accents = new Dictionary<string, (string, string)>
    {
        ["blue"] = ("#5B9BFF", "#2F5FD0"),
        ["teal"] = ("#3CCBB8", "#0F7F70"),
        ["orange"] = ("#F29B4B", "#B8560C"),
        ["purple"] = ("#C98BF0", "#8E3FB5"),
        ["pink"] = ("#F0708F", "#C2284F"),
        ["green"] = ("#8BCB5A", "#3F7F17"),
        ["gold"] = ("#E4C247", "#8A6A00"),
        ["indigo"] = ("#8B97F5", "#4350C0"),
        ["stone"] = ("#B5A394", "#6F5F50"),
        ["sky"] = ("#4FB8E8", "#0B6FA0"),
    };

    public static string AccentFor(string? name, bool dark)
    {
        if (name is not null && name.StartsWith('#')) return name;
        var key = name is not null && Accents.ContainsKey(name) ? name : "blue";
        return dark ? Accents[key].Dark : Accents[key].Light;
    }
}
