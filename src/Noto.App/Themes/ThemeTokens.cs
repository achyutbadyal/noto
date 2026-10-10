namespace Noto.App.Themes;

// Design tokens for the "Ledger" look: warm-neutral surfaces, one accent ink per workspace, and a
// single pressure ramp (amber -> rust) rather than three alarm hues. Single source: the Avalonia
// resource dictionaries are built from these, and the contrast tests read the same values.
//
// Values are verified by ThemeTests against the contrast budget (docs/07 §11.1, §11.4):
// every text token reaches 4.5:1 on canvas/surface/sidebar/raised and on hover, with the one
// documented exception (dark text-3 on hover, which renders as text-2 instead).
public static class ThemeTokens
{
    public static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string>
    {
        // Ramp, darkest to lightest: sidebar < canvas < surface < raised < border < hover.
        ["canvas"] = "#141210",
        ["surface"] = "#1B1917",
        ["sidebar"] = "#100F0D",
        ["raised"] = "#232120",
        ["hover"] = "#322E2A",
        ["border"] = "#2C2825",
        ["text-1"] = "#F2EFEA",
        ["text-2"] = "#B3ADA4",
        ["text-3"] = "#918B81",
        ["link"] = "#8FA9E8",
        ["done"] = "#8FBE77",
        ["pressure-warm"] = "#D9A441",
        ["pressure-hot"] = "#E0813C",
        ["pressure-stale"] = "#EA7C46",
    };

    public static readonly IReadOnlyDictionary<string, string> Light = new Dictionary<
        string,
        string
    >
    {
        // Ramp, darkest to lightest: border < hover < sidebar < raised < canvas < surface.
        ["canvas"] = "#F6F3EE",
        ["surface"] = "#FFFDF9",
        ["sidebar"] = "#EFEBE3",
        ["raised"] = "#F1EDE6",
        ["hover"] = "#EBE6DC",
        ["border"] = "#E0DACE",
        ["text-1"] = "#1C1A17",
        ["text-2"] = "#5C564D",
        ["text-3"] = "#6A645A",
        ["link"] = "#33529E",
        ["done"] = "#4A6B3A",
        ["pressure-warm"] = "#7E5A0C",
        ["pressure-hot"] = "#9E4A15",
        ["pressure-stale"] = "#8F3413",
    };

    // Foreground tokens that must reach WCAG AA (4.5:1) on every background token below.
    public static readonly IReadOnlyList<string> TextTokens =
    [
        "text-1",
        "text-2",
        "text-3",
        "link",
        "done",
        "pressure-warm",
        "pressure-hot",
        "pressure-stale",
    ];

    public static readonly IReadOnlyList<string> Backgrounds =
    [
        "canvas",
        "surface",
        "sidebar",
        "raised",
    ];

    // The accent a workspace gets when it has none (or one we no longer recognise).
    public const string DefaultAccent = "ink";

    // Ten workspace accents ("editorial inks"), tuned per theme. Used for bars and accents
    // (non-text, 3:1 against canvas and surface).
    public static readonly IReadOnlyDictionary<string, (string Dark, string Light)> Accents =
        new Dictionary<string, (string, string)>
        {
            ["ink"] = ("#7E9BF0", "#3A5CCC"),
            ["rust"] = ("#E08A5A", "#B4531F"),
            ["moss"] = ("#8FBE77", "#4A6B3A"),
            ["ochre"] = ("#E0C24F", "#8A6A00"),
            ["plum"] = ("#C88BC8", "#7A3E7A"),
            ["teal"] = ("#4FC9C0", "#0F6F6B"),
            ["clay"] = ("#D19A82", "#8A5340"),
            ["slate"] = ("#A9B2C0", "#4A5568"),
            ["wine"] = ("#E07A9A", "#8A2B4A"),
            ["olive"] = ("#B7C56A", "#5F6B1A"),
        };

    // Normalises whatever a workspace stores — an accent name, or a literal #rrggbb — to a stable key.
    // A literal colour is its own key, so it is shared and repainted like any other.
    public static string AccentKey(string? name) =>
        name is not null && name.StartsWith('#') ? name.ToUpperInvariant()
        : name is not null && Accents.ContainsKey(name) ? name
        : DefaultAccent;

    public static string AccentFor(string? name, bool dark) => AccentFor(name, dark, out _);

    public static string AccentFor(string? name, bool dark, out bool isLiteral)
    {
        if (name is not null && name.StartsWith('#'))
        {
            isLiteral = true;
            return name;
        }
        isLiteral = false;
        var key = AccentKey(name);
        return dark ? Accents[key].Dark : Accents[key].Light;
    }
}
