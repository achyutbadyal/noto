using System.Globalization;
using System.Text.Json;
using Noto.Core.Ai;
using Noto.Core.Models;

namespace Noto.Providers.Ai;

// Turns a model reply into a FieldSuggestion. Everything here is defensive: a model is an untrusted
// source, so values are clamped, unknown keys are ignored and anything unusable is dropped rather than
// applied to the form. A reply that cannot be read at all raises AiSuggestionException so the UI can say
// so — a failed fill must never look like an empty one.
public static class SuggestionParser
{
    const int MaxEstimateMinutes = 24 * 60;
    const int MaxTags = 8;

    public static FieldSuggestion Parse(string reply, DateOnly today)
    {
        var json = ExtractObject(reply);
        if (json is null)
            throw new AiSuggestionException("The model didn't return any JSON to use.");

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException e)
        {
            throw new AiSuggestionException("The model's reply wasn't valid JSON.", e);
        }

        if (root.ValueKind != JsonValueKind.Object)
            throw new AiSuggestionException("The model's reply wasn't a JSON object.");

        var planned = Date(root, "planned_for");
        var suggestion = new FieldSuggestion
        {
            Title = Text(root, "title"),
            EstimateMinutes = Clamp(Number(root, "estimate_minutes"), 1, MaxEstimateMinutes),
            Priority = Clamp(Number(root, "priority"), 0, 4),
            // A past plan is meaningless on a new task, so it is dropped rather than silently shifted.
            PlannedFor = planned is { } p && p >= today ? p : null,
            DueDate = Date(root, "due_date"),
            TimeOfDay = TimeOfDayOf(Text(root, "time_of_day")),
            WaitingOn = Text(root, "waiting_on"),
            Notes = Text(root, "notes"),
            Tags = Tags(root),
            Confidence = Fraction(root, "confidence"),
            Rationale = Text(root, "rationale"),
        };
        return suggestion;
    }

    // Accepts a bare object, one wrapped in ```json fences, or one embedded in prose: take the widest
    // brace-to-brace span. This is what makes "bring your own model" survive chatty endpoints.
    static string? ExtractObject(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return null;
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        return start >= 0 && end > start ? reply[start..(end + 1)] : null;
    }

    static string? Text(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    static int? Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            // Some models answer with a quoted number.
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    static DateOnly? Date(JsonElement root, string name)
    {
        var text = Text(root, name);
        return
            text is not null
            && DateOnly.TryParseExact(
                text,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            )
            ? date
            : null;
    }

    static double Fraction(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return 0;
        var raw = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String
                when double.TryParse(
                    value.GetString(),
                    CultureInfo.InvariantCulture,
                    out var parsed
                ) => parsed,
            _ => 0,
        };
        return Math.Clamp(raw, 0, 1);
    }

    static IReadOnlyList<string> Tags(JsonElement root)
    {
        if (!root.TryGetProperty("tags", out var value) || value.ValueKind != JsonValueKind.Array)
            return [];
        return
        [
            .. value
                .EnumerateArray()
                .Where(t => t.ValueKind == JsonValueKind.String)
                .Select(t => t.GetString()!.Trim().ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Distinct()
                .Take(MaxTags),
        ];
    }

    static TimeOfDay? TimeOfDayOf(string? text) =>
        text?.ToLowerInvariant() switch
        {
            "morning" => TimeOfDay.Morning,
            "midday" or "noon" => TimeOfDay.Midday,
            "afternoon" => TimeOfDay.Afternoon,
            "evening" or "night" => TimeOfDay.Evening,
            _ => null,
        };

    static int? Clamp(int? value, int min, int max) =>
        value is { } v ? Math.Clamp(v, min, max) : null;
}
