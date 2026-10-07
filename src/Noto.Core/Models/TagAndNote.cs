namespace Noto.Core.Models;

public sealed class Tag
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
}

// Note = shutdown "remember for tomorrow"; WeeklyOutcomes = the pinned top-3 of the Weekly Review.
public enum DayNoteKind
{
    Note,
    WeeklyOutcomes,
}

public sealed class DayNote
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public DateOnly Day { get; init; }
    public DayNoteKind Kind { get; init; }
    public string Text { get; set; } = "";
}
