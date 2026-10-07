using Noto.Core.Presets;

namespace Noto.Core.Workspaces;

public sealed record WorkspaceTemplate(string Name, string Icon, string Color, Preset Preset, FocusHours? Focus = null);

public static class WorkspaceTemplates
{
    public static readonly WorkspaceTemplate Work = new("Work", "🏢", "#4F7DF3", BuiltInPresets.Sprint,
        FocusHours.Weekdays(new TimeOnly(9, 0), new TimeOnly(18, 0)));
    public static readonly WorkspaceTemplate Personal = new("Personal", "🏠", "#3FB68B", BuiltInPresets.Zen);
    public static readonly WorkspaceTemplate Health = new("Health", "💪", "#E8744F", BuiltInPresets.Habit);
    public static readonly WorkspaceTemplate SideProjects = new("Side Projects", "🛠", "#9B6BF2", BuiltInPresets.Kanban);

    public static IReadOnlyList<WorkspaceTemplate> All { get; } = [Work, Personal, Health, SideProjects];
}
