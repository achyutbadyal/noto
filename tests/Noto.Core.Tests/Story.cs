using System.Text.Json.Nodes;
using Noto.Core.Derivations;
using Noto.Core.Models;

namespace Noto.Core.Tests;

// Builds an item plus the events the command bus would have written, at noon UTC on given days.
public sealed class Story
{
    public static readonly DateOnly Oct5 = new(2026, 10, 5); // Monday
    public static DateOnly Oct(int day) => new(2026, 10, day);

    readonly Guid _workspace = Guid.CreateVersion7();
    public TodoItem Item { get; }
    public List<ItemEvent> Events { get; } = [];

    public Story(DateOnly createdOn, DateOnly? plannedFor, bool someday = false)
    {
        Item = new TodoItem
        {
            Id = Guid.CreateVersion7(), WorkspaceId = _workspace, Title = "t",
            CreatedAt = At(createdOn), CreatedTz = "UTC", PlannedFor = plannedFor, IsSomeday = someday,
        };
        Add(createdOn, ItemEventType.Created, new() { ["planned_for"] = Fmt(plannedFor), ["is_someday"] = someday });
    }

    public Story Plan(DateOnly on, DateOnly? to, string kind = "plan") =>
        Add(on, ItemEventType.Planned, new() { ["to"] = Fmt(to), ["kind"] = kind });

    public Story Someday(DateOnly on, bool value) => Add(on, ItemEventType.SomedayChanged, new() { ["to"] = value });
    public Story Wait(DateOnly on) => Add(on, ItemEventType.WaitingStarted, new() { ["on"] = "bob" });
    public Story EndWait(DateOnly on) => Add(on, ItemEventType.WaitingEnded, new() { ["via"] = "manual" });
    public Story Complete(DateOnly on, DateOnly? credited = null) =>
        Add(on, ItemEventType.Completed, new() { ["completed_on"] = Fmt(credited ?? on) });
    public Story Reopen(DateOnly on) => Add(on, ItemEventType.Reopened);
    public Story Drop(DateOnly on) => Add(on, ItemEventType.Dropped, new() { ["reason"] = "NotNeeded" });
    public Story Restore(DateOnly on) => Add(on, ItemEventType.Restored);

    public ItemTimeline Timeline() => ItemTimeline.Build(Item, Events, TimeOnly.MinValue);
    public ItemMetrics Metrics(DateOnly today) => MetricsCalculator.Compute(Timeline(), today);
    public ItemHistory History() => new(Item, Timeline());

    internal Story Add(DateOnly day, ItemEventType type, JsonObject? data = null)
    {
        Events.Add(new ItemEvent
        {
            Id = Guid.CreateVersion7(), ItemId = Item.Id, WorkspaceId = _workspace, Type = type,
            Data = data, OccurredAt = At(day), Tz = "UTC", DeviceId = Guid.Empty,
        });
        return this;
    }

    static DateTimeOffset At(DateOnly d) => new(d.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    static string? Fmt(DateOnly? d) => d?.ToString("yyyy-MM-dd");
}
