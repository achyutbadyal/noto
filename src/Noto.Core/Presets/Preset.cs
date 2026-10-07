using Noto.Core.Models;

namespace Noto.Core.Presets;

public enum Density { Compact, Comfortable, Spacious }

// A preset is pure data over Layout × Order × Pressure (docs/03). Icon is a semantic name resolved to a
// monochrome vector in the UI (never a coloured emoji).
public sealed record Preset(
    string Id, string Name, string Icon, string Description,
    Layout Layout, SortOrderMode Order, Pressure Pressure,
    IReadOnlyList<string> Widgets, Density Density = Density.Comfortable)
{
    public void ApplyTo(Workspace ws)
    {
        ws.Preset = Id;
        ws.Layout = Layout;
        ws.SortOrderMode = Order;
        ws.Pressure = Pressure;
    }
}

public static class BuiltInPresets
{
    public static readonly Preset Sprint = new("sprint", "Sprint", "sprint", "Work, daily throughput",
        Layout.List, SortOrderMode.PriorityCarry, Pressure.Honest, ["capacity", "done_today", "rollover_rate", "streak"]);

    public static readonly Preset Zen = new("zen", "Zen", "zen", "Personal, low pressure",
        Layout.List, SortOrderMode.Manual, Pressure.Gentle, ["open_count", "recent_completed"], Density.Spacious);

    public static readonly Preset Deadline = new("deadline", "Deadline", "deadline", "Exams, launches, time-boxed projects",
        Layout.Timeline, SortOrderMode.DueDate, Pressure.Honest, ["due_today", "due_week", "overdue"]);

    public static readonly Preset Habit = new("habit", "Habit", "habit", "Health, routines",
        Layout.HabitGrid, SortOrderMode.TimeOfDay, Pressure.Gentle, ["flex_streak", "heatmap"]);

    public static readonly Preset Kanban = new("kanban", "Kanban", "kanban", "Side projects, multi-phase work",
        Layout.Board, SortOrderMode.Manual, Pressure.Gentle, ["column_counts", "wip", "stuck_in_column"]);

    public static readonly Preset Accountability = new("accountability", "Accountability", "accountability", "Any workspace where you want the truth",
        Layout.List, SortOrderMode.CarryDesc, Pressure.Relentless, ["top_carry", "median_carry", "rollover_trend", "stuck_mix"]);

    public static IReadOnlyList<Preset> All { get; } = [Sprint, Zen, Deadline, Habit, Kanban, Accountability];

    public static Preset? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    // "sprint" stays "sprint"; once any control differs it reads "sprint (custom)".
    public static string Label(Workspace ws)
    {
        var baseId = ws.Preset.Replace(" (custom)", "");
        var preset = Find(baseId);
        if (preset is null) return ws.Preset;
        var matches = preset.Layout == ws.Layout && preset.Order == ws.SortOrderMode && preset.Pressure == ws.Pressure;
        return matches ? preset.Id : $"{preset.Id} (custom)";
    }
}
