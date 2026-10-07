using Noto.Core.Derivations;

namespace Noto.Core.Insights;

public static class CompletionStreak
{
    // Consecutive days (newest first) with completion_rate == 1. Undefined days are skipped, not breaking.
    // Pass only finished days: today's rate is still moving.
    public static int Current(IEnumerable<DayStats> days)
    {
        var streak = 0;
        foreach (var day in days.OrderByDescending(d => d.Day))
        {
            if (day.CompletionRate is not { } rate)
                continue;
            if (rate < 1)
                break;
            streak++;
        }
        return streak;
    }

    public static int Longest(IEnumerable<DayStats> days)
    {
        int best = 0,
            run = 0;
        foreach (var day in days.OrderBy(d => d.Day))
        {
            if (day.CompletionRate is not { } rate)
                continue;
            run = rate >= 1 ? run + 1 : 0;
            best = Math.Max(best, run);
        }
        return best;
    }
}
