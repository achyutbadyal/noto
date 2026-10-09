using System.Globalization;
using System.Text.Json.Nodes;
using Noto.Core.Models;

namespace Noto.Core.Sync;

// Maps entities to/from the JSON rows that sync. Column names follow docs/04. Local-only fields
// (Workspace.SyncEnabled) are deliberately absent so they never leave the device.
public static class SyncRows
{
    public static readonly IReadOnlyList<string> ItemFields =
    [
        "workspace_id",
        "parent_id",
        "is_container",
        "title",
        "notes",
        "status",
        "is_someday",
        "planned_for",
        "due_date",
        "waiting_on",
        "drop_reason",
        "estimate_minutes",
        "priority",
        "time_of_day",
        "board_column",
        "manual_rank",
        "recurrence_rule_id",
        "occurrence_date",
        "completed_on",
        "created_at",
        "created_tz",
        "completed_at",
        "dropped_at",
        "deleted_at",
    ];

    public static readonly IReadOnlyList<string> WorkspaceFields =
    [
        "name",
        "icon",
        "color",
        "time_zone",
        "tz_follows_device",
        "day_boundary",
        "focus_hours",
        "capacity_unit",
        "daily_capacity",
        "preset",
        "layout",
        "sort_order_mode",
        "pressure",
        "pressure_overrides",
        "layout_settings",
        "now_item_id",
        "sort_rank",
        "created_at",
        "archived_at",
        "deleted_at",
    ];

    public static readonly IReadOnlyList<string> EventFields =
    [
        "item_id",
        "workspace_id",
        "type",
        "data",
        "occurred_at",
        "tz",
        "device_id",
        "hlc",
    ];

    public static readonly IReadOnlyList<string> RuleFields =
    [
        "workspace_id",
        "rrule",
        "template",
        "missed_behavior",
        "target_count",
        "target_period",
        "start_date",
        "end_date",
        "deleted_at",
    ];

    public static readonly IReadOnlyList<string> TagFields = ["workspace_id", "name", "color"];

    // todo_tag rows are hard-deleted locally; `removed_at` is the synced tombstone.
    public static readonly IReadOnlyList<string> TodoTagFields =
    [
        "item_id",
        "tag_id",
        "removed_at",
    ];

    public static readonly IReadOnlyList<string> DayNoteFields =
    [
        "workspace_id",
        "day",
        "kind",
        "text",
    ];

    public static readonly IReadOnlyList<string> LinkFields =
    [
        "item_id",
        "url",
        "position",
        "source",
        "created_at",
        "deleted_at",
    ];

    public static IReadOnlyList<string>? FieldsOf(string entityType) =>
        SyncEntities.ByType.TryGetValue(entityType, out var e) ? e.Fields : null;

    public static JsonObject ToRow(TodoItem i) =>
        new()
        {
            ["id"] = i.Id.ToString(),
            ["workspace_id"] = i.WorkspaceId.ToString(),
            ["parent_id"] = G(i.ParentId),
            ["is_container"] = i.IsContainer,
            ["title"] = i.Title,
            ["notes"] = i.Notes,
            ["status"] = i.Status.ToString(),
            ["is_someday"] = i.IsSomeday,
            ["planned_for"] = D(i.PlannedFor),
            ["due_date"] = D(i.DueDate),
            ["waiting_on"] = i.WaitingOn,
            ["drop_reason"] = i.DropReason?.ToString(),
            ["estimate_minutes"] = i.EstimateMinutes,
            ["priority"] = i.Priority,
            ["time_of_day"] = i.TimeOfDay?.ToString(),
            ["board_column"] = i.BoardColumn,
            ["manual_rank"] = i.ManualRank,
            ["recurrence_rule_id"] = G(i.RecurrenceRuleId),
            ["occurrence_date"] = D(i.OccurrenceDate),
            ["completed_on"] = D(i.CompletedOn),
            ["created_at"] = T(i.CreatedAt),
            ["created_tz"] = i.CreatedTz,
            ["completed_at"] = T(i.CompletedAt),
            ["dropped_at"] = T(i.DroppedAt),
            ["deleted_at"] = T(i.DeletedAt),
        };

    // Missing fields (partial rows built from sparse set ops) fall back to defaults.
    public static TodoItem ToItem(JsonObject r) =>
        new()
        {
            Id = Guid.Parse(r["id"]!.GetValue<string>()),
            WorkspaceId = Guid.Parse(Str(r, "workspace_id") ?? Guid.Empty.ToString()),
            ParentId = Gu(r, "parent_id"),
            IsContainer = Bool(r, "is_container"),
            Title = Str(r, "title") ?? "",
            Notes = Str(r, "notes"),
            Status = Enum.Parse<ItemStatus>(Str(r, "status") ?? nameof(ItemStatus.Open)),
            IsSomeday = Bool(r, "is_someday"),
            PlannedFor = Da(r, "planned_for"),
            DueDate = Da(r, "due_date"),
            WaitingOn = Str(r, "waiting_on"),
            DropReason = Str(r, "drop_reason") is { } dr ? Enum.Parse<DropReason>(dr) : null,
            EstimateMinutes = Int(r, "estimate_minutes"),
            Priority = Int(r, "priority") ?? 0,
            TimeOfDay = Str(r, "time_of_day") is { } tod ? Enum.Parse<TimeOfDay>(tod) : null,
            BoardColumn = Str(r, "board_column"),
            ManualRank = Str(r, "manual_rank") ?? "",
            RecurrenceRuleId = Gu(r, "recurrence_rule_id"),
            OccurrenceDate = Da(r, "occurrence_date"),
            CompletedOn = Da(r, "completed_on"),
            CreatedAt = Ti(r, "created_at") ?? DateTimeOffset.UnixEpoch,
            CreatedTz = Str(r, "created_tz") ?? "UTC",
            CompletedAt = Ti(r, "completed_at"),
            DroppedAt = Ti(r, "dropped_at"),
            DeletedAt = Ti(r, "deleted_at"),
        };

    public static JsonObject ToRow(Workspace w) =>
        new()
        {
            ["id"] = w.Id.ToString(),
            ["name"] = w.Name,
            ["icon"] = w.Icon,
            ["color"] = w.Color,
            ["time_zone"] = w.TimeZone,
            ["tz_follows_device"] = w.TzFollowsDevice,
            ["day_boundary"] = w.DayBoundary.ToString("HH:mm", CultureInfo.InvariantCulture),
            ["focus_hours"] = w.FocusHoursJson,
            ["capacity_unit"] = w.CapacityUnit.ToString(),
            ["daily_capacity"] = w.DailyCapacity,
            ["preset"] = w.Preset,
            ["layout"] = w.Layout.ToString(),
            ["sort_order_mode"] = w.SortOrderMode.ToString(),
            ["pressure"] = w.Pressure.ToString(),
            ["pressure_overrides"] = w.PressureOverridesJson,
            ["layout_settings"] = w.LayoutSettingsJson,
            ["now_item_id"] = G(w.NowItemId),
            ["sort_rank"] = w.SortRank,
            ["created_at"] = T(w.CreatedAt),
            ["archived_at"] = T(w.ArchivedAt),
            ["deleted_at"] = T(w.DeletedAt),
        };

    // `syncEnabled` carries the device-local toggle: kept from the existing row, true for rows first seen via sync.
    public static Workspace ToWorkspace(JsonObject r, bool syncEnabled) =>
        new()
        {
            Id = Guid.Parse(r["id"]!.GetValue<string>()),
            Name = Str(r, "name") ?? "",
            Icon = Str(r, "icon") ?? "",
            Color = Str(r, "color") ?? "",
            TimeZone = Str(r, "time_zone") ?? "UTC",
            TzFollowsDevice = r["tz_follows_device"]?.GetValue<bool>() ?? true,
            DayBoundary = TimeOnly.ParseExact(
                Str(r, "day_boundary") ?? "00:00",
                "HH:mm",
                CultureInfo.InvariantCulture
            ),
            FocusHoursJson = Str(r, "focus_hours"),
            CapacityUnit = Enum.Parse<CapacityUnit>(
                Str(r, "capacity_unit") ?? nameof(CapacityUnit.Minutes)
            ),
            DailyCapacity = Int(r, "daily_capacity") ?? 360,
            Preset = Str(r, "preset") ?? "zen",
            Layout = Enum.Parse<Layout>(Str(r, "layout") ?? nameof(Layout.List)),
            SortOrderMode = Enum.Parse<SortOrderMode>(
                Str(r, "sort_order_mode") ?? nameof(SortOrderMode.Manual)
            ),
            Pressure = Enum.Parse<Pressure>(Str(r, "pressure") ?? nameof(Pressure.Honest)),
            PressureOverridesJson = Str(r, "pressure_overrides"),
            LayoutSettingsJson = Str(r, "layout_settings"),
            NowItemId = Gu(r, "now_item_id"),
            SyncEnabled = syncEnabled,
            SortRank = Int(r, "sort_rank") ?? 0,
            CreatedAt = Ti(r, "created_at") ?? DateTimeOffset.UnixEpoch,
            ArchivedAt = Ti(r, "archived_at"),
            DeletedAt = Ti(r, "deleted_at"),
        };

    public static JsonObject ToRow(ItemEvent e) =>
        new()
        {
            ["id"] = e.Id.ToString(),
            ["item_id"] = e.ItemId.ToString(),
            ["workspace_id"] = e.WorkspaceId.ToString(),
            ["type"] = e.Type.ToString(),
            ["data"] = e.Data?.DeepClone(),
            ["occurred_at"] = T(e.OccurredAt),
            ["tz"] = e.Tz,
            ["device_id"] = e.DeviceId.ToString(),
            ["hlc"] = e.Hlc,
        };

    public static ItemEvent ToEvent(JsonObject r) =>
        new()
        {
            Id = Guid.Parse(r["id"]!.GetValue<string>()),
            ItemId = Guid.Parse(r["item_id"]!.GetValue<string>()),
            WorkspaceId = Guid.Parse(r["workspace_id"]!.GetValue<string>()),
            Type = Enum.Parse<ItemEventType>(r["type"]!.GetValue<string>()),
            Data = r["data"] is JsonObject d ? (JsonObject)d.DeepClone() : null,
            OccurredAt = Ti(r, "occurred_at")!.Value,
            Tz = Str(r, "tz") ?? "UTC",
            DeviceId = Guid.Parse(r["device_id"]!.GetValue<string>()),
            Hlc = Str(r, "hlc") ?? "",
        };

    // Fields whose value differs between two rows of the same entity.
    public static IEnumerable<(string Field, JsonNode? Value)> Diff(
        JsonObject before,
        JsonObject after,
        IReadOnlyList<string> fields
    )
    {
        foreach (var f in fields)
            if (!JsonNode.DeepEquals(before[f], after[f]))
                yield return (f, after[f]?.DeepClone());
    }

    static JsonNode? G(Guid? g) => g?.ToString();

    static JsonNode? D(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static JsonNode? T(DateTimeOffset? t) =>
        t?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    static string? Str(JsonObject r, string f) => r[f] is { } n ? n.GetValue<string>() : null;

    static bool Bool(JsonObject r, string f) => r[f]?.GetValue<bool>() ?? false;

    static int? Int(JsonObject r, string f) => r[f]?.GetValue<int>();

    static Guid? Gu(JsonObject r, string f) => Str(r, f) is { } s ? Guid.Parse(s) : null;

    static DateOnly? Da(JsonObject r, string f) =>
        Str(r, f) is { } s
            ? DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

    static DateTimeOffset? Ti(JsonObject r, string f) =>
        Str(r, f) is { } s ? DateTimeOffset.Parse(s, CultureInfo.InvariantCulture) : null;
}
