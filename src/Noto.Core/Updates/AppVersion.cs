using System.Globalization;

namespace Noto.Core.Updates;

// A semantic version (major.minor.patch[-prerelease]), enough to order releases. Build metadata after "+"
// is dropped because it never affects precedence. Parses release tags ("v1.2.3") and the assembly's
// informational version ("1.2.3+abc123") alike.
public sealed record AppVersion(int Major, int Minor, int Patch, string? Prerelease = null)
    : IComparable<AppVersion>
{
    public bool IsPrerelease => Prerelease is not null;

    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default!;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];
        var plus = s.IndexOf('+');
        if (plus >= 0)
            s = s[..plus];

        string? pre = null;
        var dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..];
            s = s[..dash];
            if (pre.Length == 0)
                return false;
        }

        var parts = s.Split('.');
        if (parts.Length != 3)
            return false;
        if (
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var minor
            )
            || !int.TryParse(
                parts[2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var patch
            )
        )
            return false;

        version = new AppVersion(major, minor, patch, pre);
        return true;
    }

    public static AppVersion Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"Not a version: '{text}'");

    // SemVer precedence: a prerelease sorts before its release, and prerelease identifiers compare
    // field by field (numeric fields numerically, and below alphanumeric ones).
    public int CompareTo(AppVersion? other)
    {
        if (other is null)
            return 1;
        var c = Major.CompareTo(other.Major);
        if (c != 0)
            return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0)
            return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0)
            return c;
        if (Prerelease is null)
            return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null)
            return -1;
        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    static int ComparePrerelease(string a, string b)
    {
        var x = a.Split('.');
        var y = b.Split('.');
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            var xNum = long.TryParse(
                x[i],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var xn
            );
            var yNum = long.TryParse(
                y[i],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var yn
            );
            var c =
                xNum && yNum ? xn.CompareTo(yn)
                : xNum ? -1
                : yNum ? 1
                : string.CompareOrdinal(x[i], y[i]);
            if (c != 0)
                return c;
        }
        return x.Length.CompareTo(y.Length);
    }

    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;

    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;

    public static bool operator >=(AppVersion a, AppVersion b) => a.CompareTo(b) >= 0;

    public static bool operator <=(AppVersion a, AppVersion b) => a.CompareTo(b) <= 0;

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (Prerelease is null ? "" : "-" + Prerelease);
}
