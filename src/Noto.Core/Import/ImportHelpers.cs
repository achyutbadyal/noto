using System.Globalization;

namespace Noto.Core.Import;

static class ImportHelpers
{
    static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "d MMM yyyy",
        "MMM d yyyy",
        "MMM d, yyyy",
        "d MMMM yyyy",
        "MMMM d yyyy",
        "MMMM d, yyyy",
        "yyyyMMdd",
        "M/d/yyyy",
    ];

    // Accepts "2026-10-10", "2026-10-10T09:00:00+0000", "10 Oct 2026", "Oct 10 2026"… Returns null when unrecognised
    // (e.g. Todoist's "every Monday").
    public static DateOnly? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var t = text.Trim();
        if (
            t.Length >= 10
            && char.IsDigit(t[0])
            && t[4] == '-'
            && DateOnly.TryParseExact(
                t[..10],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var iso
            )
        )
            return iso;
        if (t.Length >= 9 && t[8] == 'T' && t[..8].All(char.IsDigit))
            t = t[..8]; // iCalendar 20261009T120000Z
        return DateOnly.TryParseExact(
            t,
            DateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var d
        )
            ? d
            : null;
    }

    public static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static IReadOnlyList<string> SplitTags(string? s) =>
        string.IsNullOrWhiteSpace(s)
            ? []
            : s.Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
}
