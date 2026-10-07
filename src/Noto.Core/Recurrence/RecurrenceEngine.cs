using System.Text.Json.Nodes;
using Noto.Core.Models;
using Noto.Core.Time;

namespace Noto.Core.Recurrence;

// Lazy, deterministic instance generation (docs/04 §2.5). Pure: callers supply what already exists.
public static class RecurrenceEngine
{
    const int MaxLookbackDays = 400;

    public static Guid InstanceId(RecurrenceRule rule, DateOnly day) =>
        Uuid5.ForOccurrence(rule.Id, day);

    // Dates that need an instance today. `exists` reports instances already stored (including deleted ones).
    //  - both behaviors: today's occurrence
    //  - carry: also the single most recent missed occurrence, planned for its own date so it carries
    //  - skip: past occurrences are never created
    public static IReadOnlyList<DateOnly> DatesToGenerate(
        RecurrenceRule rule,
        DateOnly today,
        Func<DateOnly, bool> exists
    )
    {
        if (rule.DeletedAt is not null)
            return [];
        var rrule = RRule.Parse(rule.RRule);
        var dates = new List<DateOnly>();

        if (Active(rule, today) && rrule.Occurs(today, rule.StartDate) && !exists(today))
            dates.Add(today);

        if (rule.MissedBehavior == MissedBehavior.Carry)
        {
            var floor =
                rule.StartDate > today.AddDays(-MaxLookbackDays)
                    ? rule.StartDate
                    : today.AddDays(-MaxLookbackDays);
            var last = rule.EndDate is { } end && end < today.AddDays(-1) ? end : today.AddDays(-1);
            for (var d = last; d >= floor; d = d.AddDays(-1))
            {
                if (!rrule.Occurs(d, rule.StartDate))
                    continue;
                if (!exists(d))
                    dates.Insert(0, d);
                break; // only the most recent past occurrence matters
            }
        }
        return dates;
    }

    // Occurrences before `today` without a done instance. Derived, never stored.
    public static IReadOnlyList<DateOnly> Missed(
        RecurrenceRule rule,
        DateOnly from,
        DateOnly today,
        Func<DateOnly, bool> isDone
    )
    {
        var rrule = RRule.Parse(rule.RRule);
        var last = today.AddDays(-1);
        if (rule.EndDate is { } end && end < last)
            last = end;
        return rrule.Between(from, last, rule.StartDate).Where(d => !isDone(d)).ToList();
    }

    public static bool Active(RecurrenceRule rule, DateOnly day) =>
        rule.DeletedAt is null
        && day >= rule.StartDate
        && (rule.EndDate is null || day <= rule.EndDate);

    public static TodoItem BuildInstance(
        RecurrenceRule rule,
        DateOnly day,
        Workspace ws,
        TimeZoneInfo tz
    )
    {
        var t = rule.Template;
        return new TodoItem
        {
            Id = InstanceId(rule, day),
            WorkspaceId = rule.WorkspaceId,
            Title = t.Title,
            Notes = t.Notes,
            EstimateMinutes = t.EstimateMinutes,
            Priority = t.Priority,
            TimeOfDay = t.TimeOfDay,
            PlannedFor = day,
            RecurrenceRuleId = rule.Id,
            OccurrenceDate = day,
            CreatedAt = DayStart(day, ws.DayBoundary, tz),
            CreatedTz = tz.Id,
        };
    }

    // Idempotent id so devices generating the same instance emit the same event.
    public static ItemEvent CreatedEvent(TodoItem instance) =>
        new()
        {
            Id = Uuid5.Create(instance.Id, "created"),
            ItemId = instance.Id,
            WorkspaceId = instance.WorkspaceId,
            Type = ItemEventType.Created,
            Data = new JsonObject
            {
                ["planned_for"] = instance.PlannedFor?.ToString("yyyy-MM-dd"),
                ["is_someday"] = false,
                ["source"] = "recurrence",
            },
            OccurredAt = instance.CreatedAt,
            Tz = instance.CreatedTz,
            DeviceId = Guid.Empty,
        };

    // First instant of the logical day. A boundary inside a DST gap starts the day at the next valid instant.
    public static DateTimeOffset DayStart(DateOnly day, TimeOnly boundary, TimeZoneInfo tz)
    {
        var local = day.ToDateTime(boundary);
        if (tz.IsInvalidTime(local))
            local = local.AddHours(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, tz), TimeSpan.Zero);
    }
}
