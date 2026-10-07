namespace Noto.App.Logic;

public static class Duration
{
    // "1h", "45m", "1h 30m"
    public static string Short(int minutes)
    {
        var (h, m) = (minutes / 60, minutes % 60);
        return h == 0 ? $"{m}m" : m == 0 ? $"{h}h" : $"{h}h {m}m";
    }

    // "1 hour", "45 minutes", "1 hour 30 minutes" (screen readers)
    public static string Spoken(int minutes)
    {
        var (h, m) = (minutes / 60, minutes % 60);
        var hours = h == 1 ? "1 hour" : $"{h} hours";
        var mins = m == 1 ? "1 minute" : $"{m} minutes";
        return h == 0 ? mins : m == 0 ? hours : $"{hours} {mins}";
    }
}
