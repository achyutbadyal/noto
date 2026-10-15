using System.Globalization;
using Noto.Core.Derivations;
using Noto.Core.Models;
using Noto.Core.Text;

namespace Noto.App.Logic;

// One small fact about an item, shown as a chip: "Due Fri", "Afternoon", "Waiting on Priya".
// `Kind` picks the tone: "late" is the warm ramp, the rest are quiet.
public sealed record RowChip(string Text, string Kind = "plain")
{
    public bool IsLate => Kind == "late";
    public bool IsDue => Kind == "due";
}

// What the day-ahead rows and the hover card say about an item, derived from the item alone. The Today list
// stays terse on purpose; these are for the screens that have room,
public static class ItemDetail
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // "today", "tomorrow", "Fri" within the coming week, else "Oct 17" (with the year when it is not this one).
    public static string DayWord(DateOnly day, DateOnly today)
    {
        var delta = day.DayNumber - today.DayNumber;
        return delta switch
        {
            0 => "today",
            1 => "tomorrow",
            -1 => "yesterday",
            > 1 and < 7 => day.ToString("ddd", Invariant),
            _ when day.Year == today.Year => day.ToString("MMM d", Invariant),
            _ => day.ToString("MMM d, yyyy", Invariant),
        };
    }

    public static IReadOnlyList<RowChip> Chips(TodoItem item, DateOnly today, string? progress)
    {
        var chips = new List<RowChip>();
        var live = item.Status is ItemStatus.Open or ItemStatus.Waiting;
        if (item.TimeOfDay is { } part && live)
            chips.Add(new RowChip(part.ToString(), "part"));
        if (item.DueDate is { } due && live)
            chips.Add(
                due < today
                    ? new RowChip($"Overdue · {DayWord(due, today)}", "late")
                    : new RowChip($"Due {DayWord(due, today)}", "due")
            );
        if (item.Status == ItemStatus.Waiting && !string.IsNullOrWhiteSpace(item.WaitingOn))
            chips.Add(new RowChip($"Waiting on {item.WaitingOn}"));
        if (item.RecurrenceRuleId is not null)
            chips.Add(new RowChip("Repeats"));
        if (!string.IsNullOrEmpty(progress))
            chips.Add(new RowChip(progress));
        return chips;
    }

    // The first line that says something, with list and heading markers removed, cut to one tidy line.
    public static string NotesPreview(string? notes, int max = 140)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return "";
        foreach (var raw in notes.Split('\n'))
        {
            var line = raw.Trim().TrimStart('#', '>', '-', '*', ' ');
            if (
                line.StartsWith("[ ]") || line.StartsWith("[x]", StringComparison.OrdinalIgnoreCase)
            )
                line = line[3..].Trim();
            if (line.Length == 0)
                continue;
            return line.Length <= max ? line : line[..(max - 1)].TrimEnd() + "…";
        }
        return "";
    }
}
