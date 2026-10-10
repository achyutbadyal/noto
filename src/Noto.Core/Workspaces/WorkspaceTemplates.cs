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
    // Color is an accent name resolved per light/dark by the theme (ThemeTokens.Accents), not a literal
    // hex — a hex would be frozen to one theme. Icon is a semantic name resolved to a monochrome vector
    // in the UI (never a coloured emoji).
    public static readonly WorkspaceTemplate Work = new(
        "Work",
        "work",
        "ink",
        BuiltInPresets.Sprint,
        FocusHours.Weekdays(new TimeOnly(9, 0), new TimeOnly(18, 0))
    );
    public static readonly WorkspaceTemplate Personal = new(
        "Personal",
        "personal",
        "moss",
        BuiltInPresets.Zen
    );
    public static readonly WorkspaceTemplate Health = new(
        "Health",
        "health",
        "rust",
        BuiltInPresets.Habit
    );
    public static readonly WorkspaceTemplate SideProjects = new(
        "Side Projects",
        "side",
        "plum",
        BuiltInPresets.Kanban
    );

    public static IReadOnlyList<WorkspaceTemplate> All { get; } =
    [Work, Personal, Health, SideProjects];
}
