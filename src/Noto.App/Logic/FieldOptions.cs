using Noto.Core.Commands;
using Noto.Core.Models;

namespace Noto.App.Logic;

// A pickable value plus the words a person reads. ToString returns the label so a ComboBox renders it
// without needing a template (the same trick EnumOption uses).
public sealed record DurationOption(int? Minutes, string Label)
{
    public override string ToString() => Label;
}

public sealed record PriorityOption(int Value, string Label)
{
    public override string ToString() => Label;
}

// `OffsetDays` is relative to today and resolved when applied, so "Tomorrow" never goes stale.
public sealed record WhenOption(string Label, int? OffsetDays, PlanKind? Kind, bool Someday = false)
{
    public override string ToString() => Label;
}

public sealed record DueOption(string Label, int? InDays)
{
    public override string ToString() => Label;
}

public sealed record TimeOfDayOption(TimeOfDay? Value, string Label)
{
    public override string ToString() => Label;
}

// The option lists shared by the capture form and the inspector, so both offer the same choices in the
// same words (docs/07 §7.1). Anything not in the list still round-trips: DurationOption.For keeps the
// real value with a formatted label.
public static class FieldOptions
{
    public static IReadOnlyList<DurationOption> Durations { get; } =
    [
        new(null, "No estimate"),
        new(15, "15 min"),
        new(30, "30 min"),
        new(45, "45 min"),
        new(60, "1 hr"),
        new(90, "1.5 hr"),
        new(120, "2 hr"),
        new(180, "3 hr"),
        new(240, "4 hr"),
    ];

    public static DurationOption DurationFor(int? minutes) =>
        minutes is null
            ? Durations[0]
            : Durations.FirstOrDefault(o => o.Minutes == minutes)
                ?? new DurationOption(minutes, Duration.Short(minutes.Value));

    public static IReadOnlyList<PriorityOption> Priorities { get; } =
    [
        new(0, "No priority"),
        new(1, "Priority 1 — highest"),
        new(2, "Priority 2"),
        new(3, "Priority 3"),
        new(4, "Priority 4 — lowest"),
    ];

    public static PriorityOption PriorityFor(int value) =>
        Priorities.FirstOrDefault(o => o.Value == value) ?? Priorities[0];

    // Planned date. `Day` is relative to today and resolved when applied.
    public static IReadOnlyList<WhenOption> Whens { get; } =
    [
        new("Today", 0, PlanKind.KeepToday),
        new("Tomorrow", 1, PlanKind.Defer),
        new("In a week", 7, PlanKind.Defer),
        new("Someday", null, null, Someday: true),
        new("Unscheduled", null, PlanKind.Unschedule),
    ];

    public static WhenOption WhenFor(TodoItem item, DateOnly today)
    {
        if (item.IsSomeday)
            return Whens[3];
        if (item.PlannedFor is not { } planned)
            return Whens[4];
        var days = planned.DayNumber - today.DayNumber;
        return days <= 0 ? Whens[0]
            : days == 1 ? Whens[1]
            : Whens[2];
    }

    public static IReadOnlyList<DueOption> Dues { get; } =
    [
        new("No due date", null),
        new("Due today", 0),
        new("Due tomorrow", 1),
        new("Due in 3 days", 3),
        new("Due in a week", 7),
        new("Due in a month", 30),
    ];

    // The exact date never matches a preset once time has passed, so "no due date" is the only safe default.
    public static DueOption DueFor(DateOnly? due, DateOnly today) =>
        due is null
            ? Dues[0]
            : Dues.FirstOrDefault(o => o.InDays is { } d && today.AddDays(d) == due)
                ?? new DueOption(
                    $"Due {due.Value.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture)}",
                    due.Value.DayNumber - today.DayNumber
                );

    public static IReadOnlyList<TimeOfDayOption> TimesOfDay { get; } =
    [
        new(null, "Anytime"),
        new(TimeOfDay.Morning, "Morning"),
        new(TimeOfDay.Midday, "Midday"),
        new(TimeOfDay.Afternoon, "Afternoon"),
        new(TimeOfDay.Evening, "Evening"),
    ];

    public static TimeOfDayOption TimeOfDayFor(TimeOfDay? value) =>
        TimesOfDay.FirstOrDefault(o => o.Value == value) ?? TimesOfDay[0];
}
