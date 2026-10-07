namespace Noto.App.Themes;

// Design tokens for a native-feeling macOS look. Single source: the Avalonia resource dictionaries are
// built from these, and the contrast tests read the same values. Values are tuned to Apple's system greys
// while still meeting the contrast budget in docs/07 §11.1 (verified by ThemeTests).
public static class ThemeTokens
{
    public static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string>
    {
        ["canvas"] = "#1C1C1E",
        ["surface"] = "#232326",
        ["sidebar"] = "#26262A",
        ["raised"] = "#2C2C2E",
        ["hover"] = "#38383C",
        ["border"] = "#35353A",
        ["text-1"] = "#F5F5F7",
        ["text-2"] = "#A6A6AC",
        ["text-3"] = "#9A9AA1",
        ["link"] = "#5CB0FF",
        ["done"] = "#34D06B",
        ["pressure-warm"] = "#F2C94C",
        ["pressure-hot"] = "#FFA657",
        ["pressure-stale"] = "#FF8078",
    };

    public static readonly IReadOnlyDictionary<string, string> Light = new Dictionary<string, string>
    {
        ["canvas"] = "#F2F2F7",
        ["surface"] = "#FFFFFF",
        ["sidebar"] = "#ECECF1",
        ["raised"] = "#ECECF0",
        ["hover"] = "#EAEAEF",
        ["border"] = "#DCDCE1",
        ["text-1"] = "#1D1D1F",
        ["text-2"] = "#5C5C61",
        ["text-3"] = "#66666B",
        ["link"] = "#0B5FCE",
        ["done"] = "#1A7036",
        ["pressure-warm"] = "#7F5200",
        ["pressure-hot"] = "#A93C0E",
        ["pressure-stale"] = "#B52B2B",
    };

    // Foreground tokens that must reach WCAG AA (4.5:1) on every background token below.
    public static readonly IReadOnlyList<string> TextTokens =
        ["text-1", "text-2", "text-3", "link", "done", "pressure-warm", "pressure-hot", "pressure-stale"];

    public static readonly IReadOnlyList<string> Backgrounds = ["canvas", "surface", "sidebar", "raised"];

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
