using Noto.Core.Derivations;

namespace Noto.Core.Insights;

// "On days like this you usually finish 5 of 8. Expect 3 to carry." (docs/07 §4.2)
public sealed record PaceForecast(int Planned, int ExpectedDone, int ExpectedCarry, int SampleDays);

public static class PaceForecaster
{
    public const int MinHistoryDays = 14;
    const int SimilarDays = 10;

    // Median `done` over the most similar past days. Same weekday is preferred, then closest load.
    public static PaceForecast? Forecast(DateOnly day, int plannedCount, IEnumerable<DayStats> history)
    {
        var past = history.Where(d => d.Day < day && Load(d) > 0).ToList();
        if (past.Count < MinHistoryDays) return null;

        var similar = past
            .OrderBy(d => d.Day.DayOfWeek == day.DayOfWeek ? 0 : 1)
            .ThenBy(d => Math.Abs(Load(d) - plannedCount))
            .ThenByDescending(d => d.Day)
            .Take(SimilarDays)
            .Select(d => d.Done)
            .Order()
            .ToList();

        var done = (int)Math.Round(Median(similar));
        done = Math.Min(done, plannedCount);
        return new PaceForecast(plannedCount, done, plannedCount - done, similar.Count);
    }

    static int Load(DayStats d) => d.CarriedIn + d.PlannedIn + d.Added;

    static double Median(List<int> sorted) =>
        sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;
}
