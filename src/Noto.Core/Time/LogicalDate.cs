using System.Collections.Concurrent;
using Noto.Core.Models;

namespace Noto.Core.Time;

public static class LogicalDate
{
    static readonly ConcurrentDictionary<string, TimeZoneInfo> Zones = new();

    public static TimeZoneInfo Zone(string ianaId) =>
        Zones.GetOrAdd(ianaId, TimeZoneInfo.FindSystemTimeZoneById);

    // Wall-clock arithmetic, so DST gaps/overlaps never skip or repeat a logical day.
    public static DateOnly Of(DateTimeOffset instant, TimeZoneInfo tz, TimeOnly boundary)
    {
        var local = TimeZoneInfo.ConvertTime(instant, tz).DateTime;
        return DateOnly.FromDateTime(local - boundary.ToTimeSpan());
    }

    public static TimeZoneInfo EffectiveZone(Workspace ws, IClock clock) =>
        ws.TzFollowsDevice ? clock.DeviceTimeZone : Zone(ws.TimeZone);

    public static DateOnly Today(Workspace ws, IClock clock) =>
        Of(clock.UtcNow, EffectiveZone(ws, clock), ws.DayBoundary);

    // Uses the zone recorded with the instant, so history never shifts when the user travels.
    public static DateOnly Of(DateTimeOffset instant, string recordedTz, TimeOnly boundary) =>
        Of(instant, Zone(recordedTz), boundary);
}
