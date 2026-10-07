using Noto.Core.Models;
using Noto.Core.Recurrence;

namespace Noto.Core.Habits;

public enum HabitDayState { NotScheduled, Done, Missed, Pending }

public sealed record HabitCell(DateOnly Day, HabitDayState State);

public sealed record HabitStats(
    IReadOnlyList<HabitCell> Heatmap, int CurrentStreak, int LongestStreak, int ScheduledPast, int DonePast);

// Flex streaks over a rule's instances (docs/04 §4.5). All derived; nothing is stored.
public static class HabitCalculator
{
    enum PeriodState { Met, Pending, Failed }

    sealed record Period(DateOnly Start, DateOnly End, IReadOnlyList<DateOnly> Scheduled);

    // A period is a week/month when the rule has a target, otherwise each scheduled occurrence on its own.
    // Current period: met counts, unmet-but-reachable is neutral, unreachable breaks the streak.
    public static HabitStats Compute(RecurrenceRule rule, IEnumerable<TodoItem> instances, DateOnly today, DateOnly heatmapFrom)
    {
        var rrule = RRule.Parse(rule.RRule);
        var done = instances.Where(i => i.Status == ItemStatus.Done && i.OccurrenceDate is not null && i.DeletedAt is null)
            .Select(i => i.OccurrenceDate!.Value).ToHashSet();

        var horizon = rule.EndDate is { } end && end < today ? end : today;
        var heat = new List<HabitCell>();
        int scheduledPast = 0, donePast = 0;
        for (var d = heatmapFrom < rule.StartDate ? rule.StartDate : heatmapFrom; d <= today; d = d.AddDays(1))
        {
            var scheduled = RecurrenceEngine.Active(rule, d) && rrule.Occurs(d, rule.StartDate);
            var state = !scheduled ? HabitDayState.NotScheduled
                : done.Contains(d) ? HabitDayState.Done
                : d < today ? HabitDayState.Missed : HabitDayState.Pending;
            heat.Add(new HabitCell(d, state));
            if (scheduled && d < today) { scheduledPast++; if (done.Contains(d)) donePast++; }
        }

        var states = BuildPeriods(rule, rrule, horizon, today)
            .Select(p => State(p, rule, done, today)).Where(s => s is not null).Select(s => s!.Value).ToList();

        int current = 0;
        foreach (var s in Enumerable.Reverse(states))
        {
            if (s == PeriodState.Met) current++;
            else if (s == PeriodState.Failed) break;
        }

        int longest = 0, run = 0;
        foreach (var s in states)
        {
            if (s == PeriodState.Met) longest = Math.Max(longest, ++run);
            else if (s == PeriodState.Failed) run = 0;
        }

        return new HabitStats(heat, current, longest, scheduledPast, donePast);
    }

    static List<Period> BuildPeriods(RecurrenceRule rule, RRule rrule, DateOnly horizon, DateOnly today)
    {
        var periods = new List<Period>();
        if (horizon < rule.StartDate) return periods;

        if (rule.TargetCount is null || rule.TargetPeriod is null)
        {
            foreach (var d in rrule.Between(rule.StartDate, horizon, rule.StartDate)) periods.Add(new Period(d, d, [d]));
            return periods;
        }

        var cursor = PeriodStart(rule.StartDate, rule.TargetPeriod.Value);
        while (cursor <= today && cursor <= horizon)
        {
            var next = NextPeriodStart(cursor, rule.TargetPeriod.Value);
            var last = next.AddDays(-1);
            var scheduled = rrule.Between(cursor, last, rule.StartDate).Where(d => RecurrenceEngine.Active(rule, d)).ToList();
            periods.Add(new Period(cursor, last, scheduled));
            cursor = next;
        }
        return periods;
    }

    static PeriodState? State(Period p, RecurrenceRule rule, HashSet<DateOnly> done, DateOnly today)
    {
        if (p.Scheduled.Count == 0) return null; // nothing was asked of the user
        var target = Math.Min(rule.TargetCount ?? 1, p.Scheduled.Count);
        var count = p.Scheduled.Count(done.Contains);
        if (count >= target) return PeriodState.Met;
        if (p.End < today) return PeriodState.Failed;

        var stillPossible = p.Scheduled.Count(d => d >= today && !done.Contains(d));
        return count + stillPossible >= target ? PeriodState.Pending : PeriodState.Failed;
    }

    static DateOnly PeriodStart(DateOnly d, TargetPeriod period) =>
        period == TargetPeriod.Week ? RRule.MondayOf(d) : new DateOnly(d.Year, d.Month, 1);

    static DateOnly NextPeriodStart(DateOnly start, TargetPeriod period) =>
        period == TargetPeriod.Week ? start.AddDays(7) : start.AddMonths(1);
}
