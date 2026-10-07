using System.Text.Json.Nodes;
using Noto.Core.Derivations;
using Noto.Core.Insights;
using Noto.Core.Models;

namespace Noto.Core.Tests;

// Extra event builders for Story that the base helper doesn't cover.
public static class StoryExtensions
{
    public static Story Stuck(this Story s, DateOnly on, string reason) =>
        s.Raw(on, ItemEventType.StuckReasonGiven, new() { ["reason"] = reason });

    public static Story Focus(this Story s, DateOnly on, int minutes) =>
        s.Raw(on, ItemEventType.FocusStopped, new() { ["minutes"] = minutes });

    public static Story Raw(this Story s, DateOnly on, ItemEventType type, JsonObject? data = null)
    {
        s.Events.Add(
            new ItemEvent
            {
                Id = Guid.CreateVersion7(),
                ItemId = s.Item.Id,
                WorkspaceId = s.Item.WorkspaceId,
                Type = type,
                Data = data,
                OccurredAt = new DateTimeOffset(on.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
                Tz = "UTC",
                DeviceId = Guid.Empty,
            }
        );
        return s;
    }

    public static ItemRecord Record(this Story s) => new(s.Item, s.Timeline(), s.Events);

    public static IReadOnlyList<DayStats> DayStats(
        this IEnumerable<Story> stories,
        DateOnly from,
        DateOnly to
    )
    {
        var histories = stories.Select(s => s.History()).ToList();
        return Enumerable
            .Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(n => DayStatsCalculator.Compute(from.AddDays(n), histories, 30))
            .ToList();
    }
}

public static class Days
{
    public static DayStats Stats(
        DateOnly day,
        int done,
        int left,
        int carried = 0,
        int planned = 0
    ) =>
        new(
            day,
            carried,
            planned,
            0,
            done,
            0,
            0,
            left,
            done + left == 0 ? null : (double)done / (done + left),
            0
        );
}
