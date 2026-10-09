using System.Globalization;

namespace Noto.Core.Text;

// Forgiving date input for defers and capture: "fri", "next week", "+3", "tomorrow", "oct 12", "2026-10-12".
public static class NaturalDate
{
    static readonly string[] Months =
    [
        "jan",
        "feb",
        "mar",
        "apr",
        "may",
        "jun",
        "jul",
        "aug",
        "sep",
        "oct",
        "nov",
        "dec",
    ];

    public static bool TryParse(string input, DateOnly today, out DateOnly date)
    {
        date = default;
        var text = input.Trim().ToLowerInvariant();
        if (text.Length == 0)
            return false;

        switch (text)
        {
            case "today":
                date = today;
                return true;
            case "tomorrow" or "tmrw" or "tom":
                date = today.AddDays(1);
                return true;
            case "next week":
                date = NextWeekday(today, DayOfWeek.Monday, skipCurrentWeek: false);
                return true;
        }

        if (text.StartsWith('+'))
            return TryOffset(text[1..], today, out date);
        if (text.StartsWith("next ") && Weekday(text[5..]) is { } nextDay)
        {
            date = NextWeekday(today, nextDay, skipCurrentWeek: true);
            return true;
        }
        if (Weekday(text) is { } day)
        {
            date = NextWeekday(today, day, skipCurrentWeek: false);
            return true;
        }
        if (
            DateOnly.TryParseExact(
                text,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date
            )
        )
            return true;
        return TryMonthDay(text, today, out date);
    }

    // The next date strictly after today; "next <day>" means that day in the following Monday-based week.
    static DateOnly NextWeekday(DateOnly today, DayOfWeek target, bool skipCurrentWeek)
    {
        if (skipCurrentWeek)
        {
            var nextMonday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7);
            if (nextMonday == today)
                nextMonday = today.AddDays(7);
            return nextMonday.AddDays(((int)target - (int)DayOfWeek.Monday + 7) % 7);
        }
        var delta = ((int)target - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(delta == 0 ? 7 : delta);
    }

    static bool TryOffset(string text, DateOnly today, out DateOnly date)
    {
        date = default;
        var unit = text.Length > 0 && char.IsLetter(text[^1]) ? text[^1] : 'd';
        var digits = char.IsLetter(text.LastOrDefault()) ? text[..^1] : text;
        if (!int.TryParse(digits, out var n) || n < 0)
            return false;
        if (unit is not ('d' or 'w'))
            return false;
        date = today.AddDays(unit == 'w' ? n * 7 : n);
        return true;
    }

    static DayOfWeek? Weekday(string text) =>
        text switch
        {
            "mon" or "monday" => DayOfWeek.Monday,
            "tue" or "tues" or "tuesday" => DayOfWeek.Tuesday,
            "wed" or "weds" or "wednesday" => DayOfWeek.Wednesday,
            "thu" or "thur" or "thurs" or "thursday" => DayOfWeek.Thursday,
            "fri" or "friday" => DayOfWeek.Friday,
            "sat" or "saturday" => DayOfWeek.Saturday,
            "sun" or "sunday" => DayOfWeek.Sunday,
            _ => null,
        };

    // "oct 12" / "12 oct": this year, or next year if already past.
    static bool TryMonthDay(string text, DateOnly today, out DateOnly date)
    {
        date = default;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        var (monthText, dayText) = int.TryParse(parts[0], out _)
            ? (parts[1], parts[0])
            : (parts[0], parts[1]);
        var month = Array.FindIndex(Months, m => monthText.StartsWith(m)) + 1;
        if (
            month == 0
            || !int.TryParse(dayText, out var day)
            || day < 1
            || day > DateTime.DaysInMonth(2024, month)
        )
            return false;

        var candidate = new DateOnly(
            today.Year,
            month,
            Math.Min(day, DateTime.DaysInMonth(today.Year, month))
        );
        date = candidate <= today ? candidate.AddYears(1) : candidate;
        return true;
    }
}
