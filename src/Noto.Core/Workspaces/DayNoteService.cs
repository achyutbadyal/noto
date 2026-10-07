using Noto.Core.Commands;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Recurrence;

namespace Noto.Core.Workspaces;

// Shutdown's "remember for tomorrow" line and the Weekly Review's pinned top-3.
public sealed class DayNoteService(IUnitOfWork uow)
{
    public const int MaxOutcomes = 3;

    public Task SetNoteAsync(Guid workspaceId, DateOnly day, string text) => SetAsync(workspaceId, day, DayNoteKind.Note, text.Trim());

    public Task<string?> GetNoteAsync(Guid workspaceId, DateOnly day) => GetAsync(workspaceId, day, DayNoteKind.Note);

    // Outcomes are keyed to the Monday of their week and pinned to the header all week.
    public Task SetOutcomesAsync(Guid workspaceId, DateOnly weekStart, IReadOnlyList<string> outcomes)
    {
        var lines = outcomes.Select(o => o.Trim()).Where(o => o.Length > 0).Take(MaxOutcomes);
        return SetAsync(workspaceId, RRule.MondayOf(weekStart), DayNoteKind.WeeklyOutcomes, string.Join('\n', lines));
    }

    public async Task<IReadOnlyList<string>> GetOutcomesAsync(Guid workspaceId, DateOnly weekStart) =>
        (await GetAsync(workspaceId, RRule.MondayOf(weekStart), DayNoteKind.WeeklyOutcomes))?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [];

    // Deterministic id so two devices writing the same day's note converge on one row.
    static Guid IdFor(Guid workspaceId, DateOnly day, DayNoteKind kind) =>
        Uuid5.Create(workspaceId, $"day_note:{kind}:{day:yyyy-MM-dd}");

    Task SetAsync(Guid workspaceId, DateOnly day, DayNoteKind kind, string text) => uow.RunAsync(async store =>
    {
        if (await store.Workspaces.GetAsync(workspaceId) is null) throw new CommandException("Workspace not found");
        await store.DayNotes.UpsertAsync(new DayNote { Id = IdFor(workspaceId, day, kind), WorkspaceId = workspaceId, Day = day, Kind = kind, Text = text });
        return 0;
    });

    Task<string?> GetAsync(Guid workspaceId, DateOnly day, DayNoteKind kind) => uow.RunAsync(async store =>
    {
        var note = await store.DayNotes.GetAsync(workspaceId, day, kind);
        return string.IsNullOrEmpty(note?.Text) ? null : note.Text;
    });
}
