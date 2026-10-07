using System.Text.Json.Nodes;
using Noto.Core.Models;
using Noto.Core.Time;

namespace Noto.Core.Workspaces;

// Active hours for a workspace, e.g. Work Mon–Fri 09:00–18:00. Outside them it leaves Today (all) and its badges go quiet.
public sealed record FocusHours(IReadOnlyList<DayOfWeek> Days, TimeOnly Start, TimeOnly End)
{
    static readonly string[] Names = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public static FocusHours Weekdays(TimeOnly start, TimeOnly end) =>
        new(
            [
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday,
            ],
            start,
            end
        );

    public bool IsActive(DateTimeOffset instant, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTime(instant, tz);
        var time = TimeOnly.FromDateTime(local.DateTime);
        return Days.Contains(local.DayOfWeek) && time >= Start && time < End;
    }

    public string ToJson() =>
        new JsonObject
        {
            ["days"] = new JsonArray(Days.Select(d => (JsonNode)Names[(int)d]).ToArray()),
            ["start"] = Start.ToString("HH:mm"),
            ["end"] = End.ToString("HH:mm"),
        }.ToJsonString();

    public static FocusHours? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        var o = JsonNode.Parse(json)!.AsObject();
        var days = o["days"]!
            .AsArray()
            .Select(n => (DayOfWeek)Array.IndexOf(Names, n!.GetValue<string>()))
            .ToList();
        return new FocusHours(
            days,
            TimeOnly.ParseExact(o["start"]!.GetValue<string>(), "HH:mm"),
            TimeOnly.ParseExact(o["end"]!.GetValue<string>(), "HH:mm")
        );
    }

    // No focus hours means always active.
    public static bool IsActive(Workspace ws, IClock clock) =>
        FromJson(ws.FocusHoursJson)?.IsActive(clock.UtcNow, LogicalDate.EffectiveZone(ws, clock))
        ?? true;
}
