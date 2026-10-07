using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Noto.Core.Models;

public enum MissedBehavior
{
    Carry,
    Skip,
}

public enum TargetPeriod
{
    Week,
    Month,
}

public sealed class RecurrenceRule
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public string RRule { get; set; } = "FREQ=DAILY";
    public string TemplateJson { get; set; } = "{}";
    public MissedBehavior MissedBehavior { get; set; } = MissedBehavior.Carry;
    public int? TargetCount { get; set; }
    public TargetPeriod? TargetPeriod { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonIgnore]
    public RuleTemplate Template
    {
        get => RuleTemplate.FromJson(TemplateJson);
        set => TemplateJson = value.ToJson();
    }
}

// What each generated instance copies. Editing it affects instances not yet generated.
public sealed record RuleTemplate(
    string Title,
    string? Notes = null,
    int? EstimateMinutes = null,
    int Priority = 0,
    TimeOfDay? TimeOfDay = null,
    IReadOnlyList<Guid>? TagIds = null
)
{
    public string ToJson() =>
        new JsonObject
        {
            ["title"] = Title,
            ["notes"] = Notes,
            ["estimate_minutes"] = EstimateMinutes,
            ["priority"] = Priority,
            ["time_of_day"] = TimeOfDay?.ToString(),
            ["tag_ids"] = new JsonArray(
                (TagIds ?? []).Select(t => (JsonNode)t.ToString()).ToArray()
            ),
        }.ToJsonString();

    public static RuleTemplate FromJson(string json)
    {
        var o = JsonNode.Parse(json)!.AsObject();
        return new RuleTemplate(
            o["title"]?.GetValue<string>() ?? "",
            o["notes"]?.GetValue<string>(),
            o["estimate_minutes"]?.GetValue<int>(),
            o["priority"]?.GetValue<int>() ?? 0,
            o["time_of_day"]?.GetValue<string>() is { } t ? Enum.Parse<TimeOfDay>(t) : null,
            o["tag_ids"]?.AsArray().Select(n => Guid.Parse(n!.GetValue<string>())).ToList() ?? []
        );
    }
}
