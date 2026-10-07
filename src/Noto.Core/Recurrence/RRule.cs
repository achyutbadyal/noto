using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace Noto.Core.Recurrence;

public enum Freq
{
    Daily,
    Weekly,
    Monthly,
}

// Expansion is Ical.Net's (RFC 5545). Noto accepts only the subset the UI offers:
// FREQ=DAILY|WEEKLY|MONTHLY with INTERVAL, BYDAY (plain weekdays) and BYMONTHDAY.
public sealed class RRule
{
    readonly string _text;

    RRule(string text, Freq freq)
    {
        _text = text;
        Freq = freq;
    }

    public Freq Freq { get; }

    public static RRule Parse(string text)
    {
        RecurrencePattern p;
        try
        {
            p = new RecurrencePattern(text);
        }
        catch (Exception e)
        {
            throw new FormatException($"Bad RRULE '{text}': {e.Message}");
        }

        var freq = p.Frequency switch
        {
            FrequencyType.Daily => Freq.Daily,
            FrequencyType.Weekly => Freq.Weekly,
            FrequencyType.Monthly => Freq.Monthly,
            _ => throw new FormatException($"Unsupported FREQ in '{text}'"),
        };
        if (p.Interval < 1)
            throw new FormatException("INTERVAL must be ≥ 1");
        if (p.Count is not null || p.Until != default)
            throw new FormatException("COUNT/UNTIL are not supported; use the rule's end date");
        if (p.ByDay.Any(d => d.Offset is not (null or 0)))
            throw new FormatException("Ordinal BYDAY (e.g. 1MO) is not supported");
        if (p.ByMonthDay.Any(d => d == 0 || Math.Abs(d) > 31))
            throw new FormatException("Bad BYMONTHDAY");
        return new RRule(text, freq);
    }

    public bool Occurs(DateOnly date, DateOnly start) => Between(date, date, start).Any();

    public IEnumerable<DateOnly> Between(DateOnly from, DateOnly to, DateOnly start)
    {
        if (to < start)
            return [];
        var evt = new CalendarEvent
        {
            DtStart = Cal(start),
            RecurrenceRule = new RecurrencePattern(_text),
        };
        var end = to.ToDateTime(TimeOnly.MaxValue);
        return evt.GetOccurrences(Cal(from < start ? start : from))
            .Select(o => o.Period.StartTime.Value)
            .TakeWhile(t => t <= end)
            .Select(DateOnly.FromDateTime)
            .Distinct()
            .ToList();
    }

    public static DateOnly MondayOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    static CalDateTime Cal(DateOnly d) => new(d.Year, d.Month, d.Day);
}
