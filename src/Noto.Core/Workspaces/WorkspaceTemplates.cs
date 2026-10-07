using Noto.Core.Presets;

namespace Noto.Core.Workspaces;

public sealed record WorkspaceTemplate(
    string Name,
    string Icon,
    string Color,
    Preset Preset,
    FocusHours? Focus = null
);

public static class WorkspaceTemplates
{
    // Icon is a semantic name resolved to a monochrome vector in the UI (never a coloured emoji).
    public static readonly WorkspaceTemplate Work = new(
        "Work",
        "work",
        "#4F7DF3",
        BuiltInPresets.Sprint,
        FocusHours.Weekdays(new TimeOnly(9, 0), new TimeOnly(18, 0))
    );
    public static readonly WorkspaceTemplate Personal = new(
        "Personal",
        "personal",
        "#3FB68B",
        BuiltInPresets.Zen
    );
    public static readonly WorkspaceTemplate Health = new(
        "Health",
        "health",
        "#E8744F",
        BuiltInPresets.Habit
    );
    public static readonly WorkspaceTemplate SideProjects = new(
        "Side Projects",
        "side",
        "#9B6BF2",
        BuiltInPresets.Kanban
    );

    public static IReadOnlyList<WorkspaceTemplate> All { get; } =
    [Work, Personal, Health, SideProjects];
}
