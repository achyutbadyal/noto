using System.Globalization;

namespace Noto.App.Logic;

// WCAG 2.1 relative luminance and contrast ratio.
public static class Contrast
{
    public static double Ratio(string foregroundHex, string backgroundHex)
    {
        var (a, b) = (Luminance(foregroundHex), Luminance(backgroundHex));
        var (hi, lo) = a > b ? (a, b) : (b, a);
        return (hi + 0.05) / (lo + 0.05);
    }

    public static double Luminance(string hex)
    {
        var rgb = Parse(hex);
        return 0.2126 * Linear(rgb.R) + 0.7152 * Linear(rgb.G) + 0.0722 * Linear(rgb.B);
    }

    public static (byte R, byte G, byte B) Parse(string hex)
    {
        var s = hex.TrimStart('#');
        return (
            byte.Parse(s[0..2], NumberStyles.HexNumber),
            byte.Parse(s[2..4], NumberStyles.HexNumber),
            byte.Parse(s[4..6], NumberStyles.HexNumber)
        );
    }

    static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
